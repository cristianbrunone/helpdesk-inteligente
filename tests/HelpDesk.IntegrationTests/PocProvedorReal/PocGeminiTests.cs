using System.ClientModel;
using System.ClientModel.Primitives;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HelpDesk.IntegrationTests.PocProvedorReal;

/// <summary>
/// PoC da Sprint 0 (ADR-0005, ADR-0011): valida contra o provedor real, pelo endpoint OpenAI-compatível,
/// as três capacidades que o desenho exige. Não roda no CI (Category=ProvedorReal); sem chave, é pulada.
/// O texto enviado é fictício e não contém dados pessoais. A saída registra só métricas, nunca a chave.
/// </summary>
[Trait("Category", "ProvedorReal")]
public sealed class PocGeminiTests(ITestOutputHelper saida)
{
    private static readonly string[] _categorias =
        ["Acesso/Login", "Financeiro", "Bug no sistema", "Dúvida", "Infraestrutura"];

    private static readonly string[] _prioridades = ["Baixa", "Media", "Alta", "Critica"];

    private const string ChamadoFicticio =
        "Título: Erro 403 ao gerar boleto. Descrição: Desde ontem, ao clicar em 'Gerar boleto' no módulo " +
        "financeiro, o sistema mostra 'Acesso negado (403)'. Os outros módulos funcionam normalmente. " +
        "Toda a equipe de cobrança está parada.";

    public sealed record SugestaoTriagem(
        string Categoria, string Prioridade, string Resumo, string RespostaSugerida, double Confianca);

    [Fact]
    public async Task SaidaEstruturada_ComJsonSchema_RetornaTriagemValidaNoDominio()
    {
        var config = Exigir();
        using var chat = CriarChatClient(config);
        List<ChatMessage> mensagens =
        [
            new(ChatRole.System,
                "Você faz a triagem de chamados de suporte. Responda em português do Brasil. " +
                $"Categorias válidas: {string.Join(", ", _categorias)}. " +
                $"Prioridades válidas: {string.Join(", ", _prioridades)}. " +
                "O resumo tem no máximo 200 caracteres. A confiança vai de 0 a 1."),
            new(ChatRole.User, ChamadoFicticio),
        ];

        var cronometro = Stopwatch.StartNew();
        var resposta = await chat.GetResponseAsync<SugestaoTriagem>(
            mensagens, new ChatOptions { Temperature = 0.2f }, useJsonSchemaResponseFormat: true, Ct());
        cronometro.Stop();

        resposta.TryGetResult(out var sugestao).ShouldBeTrue("A resposta não pôde ser desserializada no schema.");
        sugestao.ShouldNotBeNull();
        Registrar("saída estruturada (json_schema)", config.ModeloChat, cronometro, resposta.Usage);
        saida.WriteLine(
            $"  validação: categoria='{sugestao.Categoria}', prioridade='{sugestao.Prioridade}', " +
            $"resumo={sugestao.Resumo.Length} caracteres, confiança={sugestao.Confianca:0.00}");

        _categorias.ShouldContain(sugestao.Categoria);
        _prioridades.ShouldContain(sugestao.Prioridade);
        sugestao.Resumo.Length.ShouldBeInRange(1, 200);
        sugestao.RespostaSugerida.ShouldNotBeNullOrWhiteSpace();
        sugestao.Confianca.ShouldBeInRange(0, 1);
    }

    [Fact]
    public async Task ToolCalling_ComUseFunctionInvocation_ChamaAFerramentaEUsaOResultado()
    {
        var config = Exigir();
        var chamadasDaFerramenta = 0;
        var ferramenta = AIFunctionFactory.Create(
            ([Description("Número do chamado, sem o #.")] int numero) =>
            {
                Interlocked.Increment(ref chamadasDaFerramenta);
                return new
                {
                    numero,
                    status = "Resolvido",
                    solucao = "A permissão 'emitir_boleto' foi reatribuída ao perfil Cobrança.",
                };
            },
            "buscar_chamado",
            "Busca um chamado resolvido pelo número e devolve status e solução. Somente leitura.");

        using var chat = new ChatClientBuilder(CriarChatClient(config)).UseFunctionInvocation().Build();

        var cronometro = Stopwatch.StartNew();
        var resposta = await chat.GetResponseAsync(
            "Como foi resolvido o chamado #1042? Consulte a ferramenta antes de responder.",
            new ChatOptions { Tools = [ferramenta], Temperature = 0.2f }, Ct());
        cronometro.Stop();

        var chamadasPedidasPeloModelo = resposta.Messages
            .SelectMany(m => m.Contents).OfType<FunctionCallContent>().Count();
        Registrar("tool calling (UseFunctionInvocation)", config.ModeloChat, cronometro, resposta.Usage);
        saida.WriteLine($"  validação: chamadas pedidas pelo modelo={chamadasPedidasPeloModelo}, " +
                        $"execuções da ferramenta={chamadasDaFerramenta}");

        chamadasDaFerramenta.ShouldBeGreaterThanOrEqualTo(1);
        resposta.Text.ShouldContain("permiss", Case.Insensitive);
    }

    [Fact]
    public async Task Embeddings_ComDimensions768_RetornaVetorDe768Posicoes()
    {
        var config = Exigir();
        using var gerador = CriarClienteOpenAi(config).GetEmbeddingClient(config.ModeloEmbedding).AsIEmbeddingGenerator();

        var cronometro = Stopwatch.StartNew();
        var embeddings = await gerador.GenerateAsync(
            ["Erro 403 ao gerar boleto no módulo financeiro"],
            new EmbeddingGenerationOptions { Dimensions = config.Dimensoes }, Ct());
        cronometro.Stop();

        var vetor = embeddings.Single().Vector.Span;
        var norma = 0d;
        foreach (var componente in vetor)
        {
            norma += componente * componente;
        }

        Registrar("embeddings (dimensions)", config.ModeloEmbedding, cronometro, embeddings.Usage);
        saida.WriteLine($"  validação: dimensões pedidas={config.Dimensoes}, recebidas={vetor.Length}, " +
                        $"norma L2={Math.Sqrt(norma):0.0000} (1,0000 = já normalizado)");

        vetor.Length.ShouldBe(config.Dimensoes);
    }

    private ConfiguracaoProvedorReal Exigir()
    {
        var config = ConfiguracaoProvedorReal.Carregar();
        if (config is null)
        {
            Assert.Skip("Provedor real não configurado (LLM_BASE_URL, LLM_API_KEY e modelos no .env).");
        }

        saida.WriteLine($"provedor: {config}");
        return config;
    }

    private static OpenAIClient CriarClienteOpenAi(ConfiguracaoProvedorReal config)
    {
        var opcoes = new OpenAIClientOptions
        {
            Endpoint = new Uri(config.BaseUrl),
            // Free tier: poucas requisições por minuto. Retry com backoff exponencial (respeita Retry-After)
            // para 429/5xx. Na Sprint 2 isso vira configuração (LLM_MAX_RETRIES, LLM_TIMEOUT_SECONDS).
            RetryPolicy = new ClientRetryPolicy(maxRetries: 5),
        };
        // Plano B do ADR-0005 para tool calling com Gemini 3 (thought signatures). Ver a classe.
        opcoes.AddPolicy(new PreservarAssinaturaGeminiPolicy(), PipelinePosition.PerCall);
        return new OpenAIClient(new ApiKeyCredential(config.Chave), opcoes);
    }

    private static IChatClient CriarChatClient(ConfiguracaoProvedorReal config) =>
        CriarClienteOpenAi(config).GetChatClient(config.ModeloChat).AsIChatClient();

    // Timeout próprio: o provedor real não pode travar a suíte. Inclui o tempo dos retries com backoff.
    private static CancellationToken Ct()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(120));
        return cts.Token;
    }

    // Só métricas (NFR-11): nunca o prompt, a resposta ou a chave.
    private void Registrar(string capacidade, string modelo, Stopwatch cronometro, UsageDetails? uso) =>
        saida.WriteLine($"[{capacidade}] modelo={modelo} latência={cronometro.ElapsedMilliseconds} ms " +
                        $"tokens entrada={uso?.InputTokenCount?.ToString() ?? "?"} " +
                        $"saída={uso?.OutputTokenCount?.ToString() ?? "?"}");
}
