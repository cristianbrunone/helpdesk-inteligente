using System.Diagnostics;
using HelpDesk.Application.Categorias;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Ia.Fake;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.IntegrationTests.Ia;

/// <summary>
/// O pipeline real (mascarador, prompt do arquivo, adaptador, validador) com um espião no lugar do provedor: tudo o
/// que chegaria ao LLM fica registrado e pode ser inspecionado.
/// </summary>
public sealed class PipelineTriagemTests
{
    private const string Cpf = "529.982.247-25";
    private const string Telefone = "(11) 98765-4321";
    private const string EmailNoTexto = "maria.pessoal@gmail.com";
    private const string NomeSolicitante = "Mariana Quitéria";
    private const string EmailSolicitante = "mariana.quiteria@example.com";

    private static readonly DateTimeOffset _inicio = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);
    private static readonly CategoriaResumo[] _categorias =
    [
        new(1, "Acesso/Login"), new(2, "Financeiro"), new(3, "Bug no sistema"), new(4, "Dúvida"), new(5, "Infraestrutura"),
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Critério de aceite: nenhum dado pessoal chega ao provedor (RN-10) ----------

    [Fact]
    public async Task Processar_ChamadoComDadosPessoais_NadaPessoalChegaAoProvedor()
    {
        var espiao = new ChatClientEspiao(new FakeChatClient());
        var (pipeline, triagem, chamado) = Montar(espiao);

        await pipeline.ProcessarAsync(triagem, chamado, Ct);

        var enviado = espiao.TextoEnviado;
        enviado.ShouldNotBeNullOrWhiteSpace();
        string[] proibidos =
        [
            Cpf, "52998224725", Telefone, "98765-4321", EmailNoTexto, EmailSolicitante, "Mariana", "Quitéria", "Quiteria",
        ];
        foreach (var dado in proibidos)
        {
            enviado.ShouldNotContain(dado, Case.Insensitive);
        }

        enviado.ShouldContain("[CPF]");
        enviado.ShouldContain("[TELEFONE]");
        enviado.ShouldContain("[EMAIL]");
        enviado.ShouldContain("[NOME]");
    }

    [Fact]
    public async Task Processar_ChamadoValido_ConcluiComPromptVersionadoEContextoDeTelemetria()
    {
        var espiao = new ChatClientEspiao(new FakeChatClient());
        var (pipeline, triagem, chamado) = Montar(espiao);

        var resultado = await pipeline.ProcessarAsync(triagem, chamado, Ct);

        resultado.ShouldBe(new ResultadoPipeline(StatusTriagem.Concluida, null));
        triagem.Status.ShouldBe(StatusTriagem.Concluida);
        triagem.CategoriaSugeridaId.ShouldBe((short)2);
        triagem.PromptVersao.ShouldBe("triagem.v1");
        triagem.Provedor.ShouldBe("fake");
        triagem.Modelo.ShouldBe("fake-triagem-v1");

        var opcoes = espiao.Opcoes.ShouldNotBeNull();
        opcoes.MaxOutputTokens.ShouldBe(800);
        opcoes.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>().Schema.ShouldNotBeNull();
        opcoes.AdditionalProperties![ContextoUsoLlm.TriagemId].ShouldBe(triagem.Id);
        espiao.Mensagens[0].Role.ShouldBe(ChatRole.System);
        espiao.Mensagens[0].Text.ShouldContain("- Financeiro");
        espiao.Mensagens[1].Text.ShouldStartWith("<chamado>");
    }

    // ---------- Falhas viram "Falhou" com motivo, nunca exceção ----------

    [Theory]
    [InlineData(ModoFake.JsonInvalido, "json_invalido", "A IA retornou uma resposta fora do formato esperado.")]
    [InlineData(ModoFake.CategoriaInexistente, "categoria_inexistente", "A IA sugeriu uma categoria que não existe.")]
    [InlineData(ModoFake.RateLimit, "rate_limit", "O provedor de IA atingiu o limite de uso. Tente refazer em alguns minutos.")]
    public async Task Processar_ProvedorComProblema_FalhaComMotivoAmigavel(ModoFake modo, string codigo, string mensagem)
    {
        var (pipeline, triagem, chamado) = Montar(new FakeChatClient(modo));

        var resultado = await pipeline.ProcessarAsync(triagem, chamado, Ct);

        resultado.ShouldBe(new ResultadoPipeline(StatusTriagem.Falhou, codigo));
        triagem.Status.ShouldBe(StatusTriagem.Falhou);
        triagem.ErroMotivo.ShouldBe(mensagem);
        triagem.Provedor.ShouldBe("fake");
    }

    [Fact]
    public async Task Processar_RespostaCortadaPeloLimiteDeTokens_FalhaComoTruncada()
    {
        var truncada = new ChatClientEspiao(new FakeChatClient(), finishReason: ChatFinishReason.Length);
        var (pipeline, triagem, chamado) = Montar(truncada);

        var resultado = await pipeline.ProcessarAsync(triagem, chamado, Ct);

        resultado.Codigo.ShouldBe("resposta_truncada");
        triagem.ErroMotivo.ShouldBe("A resposta da IA excedeu o limite de tamanho.");
    }

    [Fact]
    public async Task Processar_ErroDefinitivoDoProvedor_FalhaSemExporODetalhe()
    {
        var (pipeline, triagem, chamado) = Montar(new ChatClientEspiao(
            new FakeChatClient(), erro: new InvalidOperationException($"401 com detalhe interno {Cpf}")));

        var resultado = await pipeline.ProcessarAsync(triagem, chamado, Ct);

        resultado.Codigo.ShouldBe("erro");
        triagem.ErroMotivo.ShouldBe("Não foi possível obter a sugestão da IA. Tente refazer a triagem.");
        triagem.ErroMotivo!.ShouldNotContain(Cpf);
    }

    [Fact]
    public async Task Processar_ErroInesperadoForaDoProvedor_FalhaEmVezDeLancar()
    {
        var (pipeline, triagem, chamado) = Montar(new FakeChatClient(), categorias: new CategoriasQueFalham());

        var resultado = await pipeline.ProcessarAsync(triagem, chamado, Ct);

        resultado.Codigo.ShouldBe("erro_interno");
        triagem.Status.ShouldBe(StatusTriagem.Falhou);
    }

    [Fact]
    public async Task Processar_WorkerParando_PropagaOCancelamentoEMantemPendente()
    {
        var (pipeline, triagem, chamado) = Montar(new FakeChatClient(ModoFake.Lento, TimeSpan.FromSeconds(30)));
        using var cancelamento = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancelamento.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            pipeline.ProcessarAsync(triagem, chamado, cancelamento.Token));

        triagem.Status.ShouldBe(StatusTriagem.Pendente);
    }

    // ---------- Spans (ADR-0019): as 5 etapas, sem conteúdo ----------

    [Fact]
    public async Task Processar_EmiteUmSpanPorEtapaSemTextoDoChamado()
    {
        using var fonteDoTeste = new ActivitySource("Teste.Pipeline");
        var spans = new List<Activity>();
        using var ouvinte = new ActivityListener
        {
            ShouldListenTo = fonte => fonte.Name is PipelineTriagem.NomeFonteAtividades or "Teste.Pipeline",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(ouvinte);
        var (pipeline, triagem, chamado) = Montar(new FakeChatClient());

        ActivityTraceId traceDoTeste;
        using (var raiz = fonteDoTeste.StartActivity("raiz")!)
        {
            traceDoTeste = raiz.TraceId;
            await pipeline.ProcessarAsync(triagem, chamado, Ct);
        }

        var doTeste = spans.Where(s => s.TraceId == traceDoTeste && s.Source.Name == PipelineTriagem.NomeFonteAtividades)
            .ToList();
        doTeste.Select(s => s.OperationName)
            .ShouldBe(["mascarar", "recuperar", "montar_prompt", "completar", "validar"]);
        doTeste.Single(s => s.OperationName == "mascarar").GetTagItem("mascaramento.cpfs").ShouldBe(1);
        doTeste.Single(s => s.OperationName == "recuperar").GetTagItem("rag.documentos").ShouldBe(0);
        doTeste.Single(s => s.OperationName == "montar_prompt").GetTagItem("prompt.versao").ShouldBe("triagem.v1");
        doTeste.Single(s => s.OperationName == "validar").GetTagItem("validacao.resultado").ShouldBe("valida");

        var valores = doTeste.SelectMany(s => s.TagObjects).Select(t => t.Value?.ToString() ?? "");
        valores.ShouldAllBe(v => !v.Contains(Cpf) && !v.Contains("boleto") && !v.Contains(NomeSolicitante));
    }

    // ---------- Apoio ----------

    private static (PipelineTriagem, TriagemIA, Chamado) Montar(IChatClient chat, IConsultaCategorias? categorias = null)
    {
        var opcoes = new OpcoesLlm
        {
            Provedor = TipoProvedorLlm.Fake,
            ModeloChat = "nao-usado",
            Timeout = TimeSpan.FromSeconds(5),
            MaxRetries = 0,
            MaxTokensSaidaTriagem = 800,
        };
        var pipeline = new PipelineTriagem(
            new MascaradorDadosPessoais(),
            new RecuperadorSemRag(),
            new MontadorPromptTriagem(new CatalogoPromptsArquivo()),
            new ClienteLlmTriagem(chat, opcoes, NullLogger<ClienteLlmTriagem>.Instance),
            categorias ?? new CategoriasEmMemoria(),
            TimeProvider.System);

        var chamado = Chamado.Abrir(
            "Não consigo emitir o boleto",
            $"Olá, aqui é a {NomeSolicitante}. O boleto não sai. Meu CPF é {Cpf}, telefone {Telefone}, " +
            $"e-mail pessoal {EmailNoTexto}. A Quitéria do financeiro também tentou.",
            NomeSolicitante,
            EmailSolicitante,
            null,
            null,
            _inicio);
        return (pipeline, TriagemIA.Criar(chamado, _inicio), chamado);
    }

    private sealed class CategoriasEmMemoria : IConsultaCategorias
    {
        public Task<IReadOnlyList<CategoriaResumo>> ListarAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CategoriaResumo>>(_categorias);
    }

    private sealed class CategoriasQueFalham : IConsultaCategorias
    {
        public Task<IReadOnlyList<CategoriaResumo>> ListarAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Banco fora do ar.");
    }

    /// <summary>Registra tudo o que seria enviado ao provedor e delega ao cliente interno.</summary>
    private sealed class ChatClientEspiao(
        IChatClient interno,
        ChatFinishReason? finishReason = null,
        Exception? erro = null) : DelegatingChatClient(interno)
    {
        public List<ChatMessage> Mensagens { get; } = [];

        public ChatOptions? Opcoes { get; private set; }

        public string TextoEnviado => string.Join('\n', Mensagens.Select(m => m.Text));

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Mensagens.AddRange(messages);
            Opcoes = options;
            if (erro is not null)
            {
                throw erro;
            }

            var resposta = await base.GetResponseAsync(Mensagens, options, cancellationToken);
            if (finishReason is { } motivo)
            {
                resposta.FinishReason = motivo;
            }

            return resposta;
        }
    }
}
