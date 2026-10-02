using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HelpDesk.Application;
using HelpDesk.Application.Copiloto;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// O agente do copiloto sobre o <c>Microsoft.Extensions.AI</c> (ADR-0004, ADR-0005, ADR-0012): o
/// <see cref="FunctionInvokingChatClient"/> roda o laço "modelo pede ferramenta → executamos → modelo continua", com
/// no máximo <see cref="MaxRodadas"/> rodadas, por cima do mesmo cliente da triagem (resiliência, telemetria e span
/// GenAI por chamada). Traduz o stream do SDK em <see cref="PassoCopiloto"/>.
/// <para>
/// As ferramentas são as de <see cref="FerramentasCopiloto"/>, todas somente leitura. Cada execução é um span
/// <c>copiloto.ferramenta</c> com o nome, o resultado e a contagem, sem argumentos nem conteúdo (ADR-0019).
/// </para>
/// </summary>
internal sealed class CopilotoLlm : ICopilotoLlm
{
    public const int MaxRodadas = 3;

    private static readonly ActivitySource _fonte = new(ConversarComCopiloto.NomeFonteAtividades);

    private readonly IChatClient _cliente;
    private readonly OpcoesLlm _opcoes;

    public CopilotoLlm(IChatClient chat, OpcoesLlm opcoes, ILoggerFactory logs)
    {
        _opcoes = opcoes;
        // Montado uma vez: descartar este cliente descartaria o cliente compartilhado que está por baixo.
        _cliente = new ChatClientBuilder(chat)
            .UseFunctionInvocation(logs, f =>
            {
                f.MaximumIterationsPerRequest = MaxRodadas;
                // O detalhe de uma exceção inesperada não vai para o modelo (pode ter dados internos).
                f.IncludeDetailedErrors = false;
            })
            .Build();
    }

    public async IAsyncEnumerable<PassoCopiloto> ConversarAsync(
        PromptCopiloto prompt, FerramentasCopiloto ferramentas, Guid chamadoId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<ChatMessage> mensagens =
        [
            new(ChatRole.System, prompt.Sistema.Valor),
            .. prompt.Mensagens.Select(m => new ChatMessage(m.DoAtendente ? ChatRole.User : ChatRole.Assistant,
                m.Texto.Valor)),
        ];
        var opcoesChat = new ChatOptions
        {
            Tools = Ferramentas(new FuncoesCopiloto(ferramentas)),
            ToolMode = ChatToolMode.Auto,
            MaxOutputTokens = _opcoes.MaxTokensSaidaCopiloto,
        }.ParaCopiloto(chamadoId);

        var nomesPorChamada = new Dictionary<string, string>();
        long? tokensEntrada = null, tokensSaida = null;
        var truncada = false;

        await using var enumerador = _cliente.GetStreamingResponseAsync(mensagens, opcoesChat, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool temProximo;
            try
            {
                temProximo = await enumerador.MoveNextAsync();
            }
            catch (ProvedorIndisponivelException falha)
            {
                // A Application não conhece as exceções da Infrastructure: vira o 503/erro do contrato.
                throw new IaIndisponivelException(MensagemDoProvedor(falha.Tipo));
            }

            if (!temProximo)
            {
                break;
            }

            var atualizacao = enumerador.Current;
            foreach (var conteudo in atualizacao.Contents)
            {
                switch (conteudo)
                {
                    case FunctionCallContent chamada:
                        nomesPorChamada[chamada.CallId] = chamada.Name;
                        yield return new PassoFerramentaIniciada(chamada.Name);
                        break;
                    case FunctionResultContent resultado:
                        yield return new PassoFerramentaConcluida(
                            nomesPorChamada.GetValueOrDefault(resultado.CallId, "desconhecida"), Contar(resultado));
                        break;
                    case UsageContent uso:
                        tokensEntrada = Somar(tokensEntrada, uso.Details.InputTokenCount);
                        tokensSaida = Somar(tokensSaida, uso.Details.OutputTokenCount);
                        break;
                    case TextContent { Text.Length: > 0 } texto:
                        yield return new PassoTexto(texto.Text);
                        break;
                }
            }

            truncada |= atualizacao.FinishReason == ChatFinishReason.Length;
        }

        yield return new PassoFim(truncada, tokensEntrada, tokensSaida);
    }

    private static List<AITool> Ferramentas(FuncoesCopiloto funcoes) =>
    [
        Criar(funcoes.BuscarChamadosSimilaresAsync, FerramentasCopiloto.BuscarChamadosSimilares,
            "Busca chamados já resolvidos parecidos com a consulta. Use para saber se já houve casos assim e como " +
            "foram resolvidos."),
        Criar(funcoes.BuscarArtigosAsync, FerramentasCopiloto.BuscarArtigos,
            "Busca trechos de artigos da base de conhecimento (procedimentos e soluções conhecidas)."),
        Criar(funcoes.ObterHistoricoDoChamadoAsync, FerramentasCopiloto.ObterHistoricoDoChamado,
            "Lê as mudanças de status e os comentários do chamado em contexto (sempre o chamado aberto na tela)."),
        Criar(funcoes.ObterMetricasDaCategoriaAsync, FerramentasCopiloto.ObterMetricasDaCategoria,
            "Volume de chamados, tempo médio de resolução e taxa de aceitação da IA numa categoria."),
    ];

    private static AIFunction Criar(Delegate metodo, DescricaoFerramenta ferramenta, string descricao) =>
        AIFunctionFactory.Create(metodo, new AIFunctionFactoryOptions { Name = ferramenta.Nome, Description = descricao });

    /// <summary>Quantos itens a ferramenta devolveu: o tamanho da lista, 1 para um objeto, nulo para erro.</summary>
    private static int? Contar(FunctionResultContent resultado)
    {
        if (resultado.Exception is not null)
        {
            return null;
        }

        var json = resultado.Result switch
        {
            JsonElement elemento => elemento,
            null => default,
            var outro => JsonSerializer.SerializeToElement(outro, AIJsonUtilities.DefaultOptions),
        };
        return json.ValueKind switch
        {
            JsonValueKind.Array => json.GetArrayLength(),
            JsonValueKind.Object when json.TryGetProperty("erro", out _) => null,
            JsonValueKind.Object => 1,
            _ => null,
        };
    }

    private static long? Somar(long? atual, long? mais) => mais is null ? atual : (atual ?? 0) + mais;

    private static string MensagemDoProvedor(string tipo) => tipo switch
    {
        ProvedorIndisponivelException.TipoTimeout => "O provedor de IA não respondeu a tempo. Tente de novo.",
        ProvedorIndisponivelException.TipoRateLimit =>
            "O provedor de IA atingiu o limite de uso. Tente de novo em alguns minutos.",
        _ => "O provedor de IA está indisponível no momento. Tente de novo.",
    };

    /// <summary>
    /// As ferramentas como o modelo as vê: os parâmetros opcionais têm valor padrão (sem ele, o
    /// <see cref="AIFunctionFactory"/> os trata como obrigatórios). Um parâmetro inválido volta ao modelo como
    /// <c>{"erro": "..."}</c>, para ele corrigir a chamada; o span registra só o resultado.
    /// </summary>
    private sealed class FuncoesCopiloto(FerramentasCopiloto ferramentas)
    {
        public Task<object> BuscarChamadosSimilaresAsync(
            [Description("O assunto a buscar, em poucas palavras (ex.: \"erro 403 ao emitir boleto\").")] string consulta,
            [Description("Nome exato de uma categoria, para filtrar. Opcional.")] string? categoria = null,
            [Description("Quantos chamados devolver, de 1 a 5. Padrão: 3.")] int? limite = null,
            CancellationToken cancellationToken = default) =>
            ExecutarAsync(FerramentasCopiloto.BuscarChamadosSimilares.Nome, async () =>
                await ferramentas.BuscarChamadosSimilaresAsync(consulta, categoria, limite, cancellationToken));

        public Task<object> BuscarArtigosAsync(
            [Description("O assunto a buscar, em poucas palavras.")] string consulta,
            [Description("Quantos trechos devolver, de 1 a 5. Padrão: 3.")] int? limite = null,
            CancellationToken cancellationToken = default) =>
            ExecutarAsync(FerramentasCopiloto.BuscarArtigos.Nome, async () =>
                await ferramentas.BuscarArtigosAsync(consulta, limite, cancellationToken));

        public Task<object> ObterHistoricoDoChamadoAsync(CancellationToken cancellationToken = default) =>
            ExecutarAsync(FerramentasCopiloto.ObterHistoricoDoChamado.Nome, async () =>
                await ferramentas.ObterHistoricoDoChamadoAsync(cancellationToken));

        public Task<object> ObterMetricasDaCategoriaAsync(
            [Description("Nome exato da categoria.")] string categoria,
            CancellationToken cancellationToken = default) =>
            ExecutarAsync(FerramentasCopiloto.ObterMetricasDaCategoria.Nome, async () =>
                await ferramentas.ObterMetricasDaCategoriaAsync(categoria, cancellationToken));

        private static async Task<object> ExecutarAsync(string nome, Func<Task<object>> executar)
        {
            using var atividade = _fonte.StartActivity("copiloto.ferramenta");
            atividade?.SetTag("ferramenta.nome", nome);
            try
            {
                var resultado = await executar();
                atividade?.SetTag("ferramenta.resultado", "ok");
                if (resultado is System.Collections.ICollection itens)
                {
                    atividade?.SetTag("ferramenta.itens", itens.Count);
                }

                return resultado;
            }
            catch (ParametroFerramentaInvalidoException invalido)
            {
                atividade?.SetTag("ferramenta.resultado", "parametro_invalido");
                return new { erro = invalido.Message };
            }
            catch (Exception erro) when (erro is not OperationCanceledException)
            {
                atividade?.SetTag("ferramenta.resultado", "erro");
                atividade?.SetStatus(ActivityStatusCode.Error, erro.GetType().Name);
                throw;
            }
        }
    }
}
