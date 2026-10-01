using System.Diagnostics;
using System.Text;
using HelpDesk.Application.Categorias;
using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Consultas;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Evals;

/// <summary>O que varia entre as medições: o provedor, a versão do prompt e o RAG ligado ou desligado.</summary>
internal sealed record ConfiguracaoEval(
    OpcoesLlm Llm,
    OpcoesRag Rag,
    string VersaoPrompt,
    bool ComRag,
    string? ConnectionString,
    TimeSpan Intervalo);

/// <summary>
/// Executa o <b>pipeline real de produção</b> (ADR-0018): mascarador, recuperador, prompt do arquivo, cliente de
/// LLM com resiliência e telemetria, validador. Só três peças são do harness:
/// <list type="bullet">
/// <item>um espião entre a resiliência e o provedor, que guarda o texto enviado (para conferir os casos de PII);</item>
/// <item>um registro de uso em memória (o eval não grava no <c>uso_llm</c> e não suja o dashboard);</item>
/// <item>as categorias do seed, fixas (com RAG, o banco é só lido para a busca).</item>
/// </list>
/// </summary>
internal sealed class ExecutorEval(ConfiguracaoEval configuracao, ILoggerFactory logs) : IAsyncDisposable
{
    /// <summary>As cinco categorias do seed, com os IDs do seed (o validador compara pelo nome).</summary>
    public static readonly IReadOnlyList<CategoriaResumo> Categorias =
    [
        new(1, "Acesso/Login"), new(2, "Financeiro"), new(3, "Bug no sistema"), new(4, "Dúvida"), new(5, "Infraestrutura"),
    ];

    private readonly RegistroUsoEmMemoria _registro = new();
    private readonly EspiaoChatClient _espiao = new(FabricaClienteChat.Criar(configuracao.Llm));
    private HelpDeskDbContext? _db;

    public string ModeloEmbedding => configuracao.Llm.ModeloEmbeddingEfetivo;

    public async Task<IReadOnlyList<ExecucaoEval>> ExecutarAsync(
        IReadOnlyList<CasoEval> casos, int repeticoes, Action<CasoEval, ExecucaoEval>? aoExecutar,
        CancellationToken cancellationToken)
    {
        var pipeline = MontarPipeline();
        var execucoes = new List<ExecucaoEval>(casos.Count * repeticoes);
        for (var repeticao = 1; repeticao <= repeticoes; repeticao++)
        {
            foreach (var caso in casos)
            {
                if (execucoes.Count > 0 && configuracao.Intervalo > TimeSpan.Zero)
                {
                    await Task.Delay(configuracao.Intervalo, cancellationToken);
                }

                var execucao = await ExecutarAsync(pipeline, caso, repeticao, cancellationToken);
                execucoes.Add(execucao);
                aoExecutar?.Invoke(caso, execucao);
            }
        }

        return execucoes;
    }

    public async ValueTask DisposeAsync()
    {
        _espiao.Dispose();
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }

    private PipelineTriagem MontarPipeline()
    {
        var llm = configuracao.Llm;
        var mascarador = new MascaradorDadosPessoais();
        var chat = FabricaClienteChat.Montar(_espiao, llm, logs, _registro);
        var cliente = new ClienteLlmTriagem(chat, llm, logs.CreateLogger<ClienteLlmTriagem>());
        var montador = new MontadorPromptTriagem(new CatalogoPromptsArquivo(), configuracao.VersaoPrompt);

        IRecuperadorContexto recuperador = new RecuperadorSemRag();
        if (configuracao.ComRag)
        {
            var opcoesBanco = new DbContextOptionsBuilder<HelpDeskDbContext>();
            ConfiguracaoBanco.Configurar(opcoesBanco, configuracao.ConnectionString
                ?? throw new InvalidOperationException("Com RAG, o harness precisa do banco indexado."));
            _db = new HelpDeskDbContext(opcoesBanco.Options);
            var gerador = new GeradorEmbeddings(FabricaGeradorEmbeddings.Montar(llm, logs, _registro), llm);
            recuperador = new RecuperadorRag(gerador, new BuscaSemantica(_db), mascarador, configuracao.Rag,
                logs.CreateLogger<RecuperadorRag>());
        }

        return new PipelineTriagem(mascarador, recuperador, montador, cliente, new CategoriasDoSeed(),
            TimeProvider.System);
    }

    private async Task<ExecucaoEval> ExecutarAsync(
        PipelineTriagem pipeline, CasoEval caso, int repeticao, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        var chamado = Chamado.Abrir(caso.Titulo, caso.Descricao, caso.SolicitanteNome, caso.SolicitanteEmail, null,
            null, agora);
        var triagem = TriagemIA.Criar(chamado, agora);
        var usoAntes = _registro.Quantidade;
        _espiao.Limpar();

        var inicio = Stopwatch.GetTimestamp();
        var resultado = await pipeline.ProcessarAsync(triagem, chamado, cancellationToken);
        var latencia = (long)Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;

        var usos = _registro.Desde(usoAntes);
        var enviado = _espiao.TextoEnviado;
        var resposta = triagem.RespostaSugerida ?? string.Empty;
        var vazou = caso.DadosPessoais?.Any(dado =>
            enviado.Contains(dado, StringComparison.OrdinalIgnoreCase)
            || resposta.Contains(dado, StringComparison.OrdinalIgnoreCase)) ?? false;

        return new ExecucaoEval(
            caso.Id,
            repeticao,
            Valida: resultado.Status == StatusTriagem.Concluida,
            resultado.Codigo,
            Categoria: Categorias.FirstOrDefault(c => c.Id == triagem.CategoriaSugeridaId)?.Nome,
            Prioridade: triagem.PrioridadeSugerida?.ToString(),
            latencia,
            usos.Sum(u => (long)(u.TokensEntrada ?? 0)),
            usos.Sum(u => (long)(u.TokensSaida ?? 0)),
            vazou);
    }

    private sealed class CategoriasDoSeed : IConsultaCategorias
    {
        public Task<IReadOnlyList<CategoriaResumo>> ListarAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Categorias);
    }

    /// <summary>Uso do LLM e dos embeddings em memória: tokens por execução, sem gravar no banco.</summary>
    private sealed class RegistroUsoEmMemoria : IRegistroUsoLlm
    {
        private readonly List<RegistroUsoLlm> _registros = [];

        public int Quantidade => _registros.Count;

        public IReadOnlyList<RegistroUsoLlm> Desde(int indice) => _registros[indice..];

        public Task RegistrarAsync(RegistroUsoLlm registro)
        {
            _registros.Add(registro);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Guarda o texto de cada mensagem enviada ao provedor (todas as tentativas), só em memória e só durante a
/// execução, para conferir que nenhum dado pessoal saiu. Nunca vai para o relatório.
/// </summary>
internal sealed class EspiaoChatClient(IChatClient provedor) : DelegatingChatClient(provedor)
{
    private readonly StringBuilder _enviado = new();

    public string TextoEnviado => _enviado.ToString();

    public void Limpar() => _enviado.Clear();

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var mensagens = messages as IList<ChatMessage> ?? [.. messages];
        foreach (var mensagem in mensagens)
        {
            _enviado.AppendLine(mensagem.Text);
        }

        return base.GetResponseAsync(mensagens, options, cancellationToken);
    }
}
