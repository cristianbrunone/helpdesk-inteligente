using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Ia.Fake;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.UnitTests.Infraestrutura;

public sealed class ResilienciaChatClientTests
{
    private static readonly ChatMessage[] _mensagens = [new(ChatRole.User, "oi")];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<TimeSpan> _esperas = [];

    private ResilienciaChatClient Criar(IChatClient interno, int maxRetries = 3, TimeSpan? timeout = null) =>
        new(interno,
            new OpcoesLlm
            {
                Provedor = TipoProvedorLlm.Fake,
                ModeloChat = "x",
                Timeout = timeout ?? TimeSpan.FromSeconds(5),
                MaxRetries = maxRetries,
                MaxTokensSaidaTriagem = 800,
            },
            NullLogger<ResilienciaChatClient>.Instance,
            esperar: (espera, _) =>
            {
                _esperas.Add(espera);
                return Task.CompletedTask;
            },
            aleatorio: new Random(7));

    [Fact]
    public async Task Responder_PrimeiraTentativaOk_NaoRepeteNemEspera()
    {
        var interno = new ClienteRoteirizado(Ok);

        var resposta = await Criar(interno).GetResponseAsync(_mensagens, cancellationToken: Ct);

        resposta.Text.ShouldBe("ok");
        interno.Chamadas.ShouldBe(1);
        _esperas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Responder_DoisRateLimitsDepoisOk_RepeteRespeitandoORetryAfter()
    {
        var interno = new ClienteRoteirizado(RateLimit(TimeSpan.FromSeconds(5)), RateLimit(null), Ok);

        var resposta = await Criar(interno).GetResponseAsync(_mensagens, cancellationToken: Ct);

        resposta.Text.ShouldBe("ok");
        interno.Chamadas.ShouldBe(3);
        _esperas.Count.ShouldBe(2);
        _esperas[0].ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        _esperas[1].ShouldBeInRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Responder_ProvedorLentoEmTodasAsTentativas_EsgotaELancaTimeout()
    {
        var interno = new ClienteRoteirizado(Lento, Lento, Lento);

        var erro = await Should.ThrowAsync<ProvedorIndisponivelException>(() =>
            Criar(interno, maxRetries: 2, timeout: TimeSpan.FromMilliseconds(50))
                .GetResponseAsync(_mensagens, cancellationToken: Ct));

        erro.Tipo.ShouldBe("timeout");
        interno.Chamadas.ShouldBe(3);
        _esperas.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(429, "rate_limit")]
    [InlineData(500, "indisponivel")]
    [InlineData(502, "indisponivel")]
    [InlineData(503, "indisponivel")]
    [InlineData(504, "indisponivel")]
    public async Task Responder_HttpTransitorioDoSdk_RepeteEClassifica(int status, string tipo)
    {
        var interno = new ClienteRoteirizado(_ => throw Http(status), _ => throw Http(status));

        var erro = await Should.ThrowAsync<ProvedorIndisponivelException>(() =>
            Criar(interno, maxRetries: 1).GetResponseAsync(_mensagens, cancellationToken: Ct));

        erro.Tipo.ShouldBe(tipo);
        interno.Chamadas.ShouldBe(2);
    }

    [Fact]
    public async Task Responder_RetryAfterDoHttp429_EUsadoNaEspera()
    {
        var interno = new ClienteRoteirizado(_ => throw Http(429, retryAfter: "12"), Ok);

        await Criar(interno).GetResponseAsync(_mensagens, cancellationToken: Ct);

        _esperas.ShouldHaveSingleItem().ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(12));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    public async Task Responder_ErroDefinitivo_NaoRepete(int status)
    {
        var interno = new ClienteRoteirizado(_ => throw Http(status), Ok);

        await Should.ThrowAsync<ClientResultException>(() =>
            Criar(interno).GetResponseAsync(_mensagens, cancellationToken: Ct));

        interno.Chamadas.ShouldBe(1);
    }

    [Fact]
    public async Task Responder_CancelamentoExterno_SobeSemNovasTentativas()
    {
        using var cancelamento = new CancellationTokenSource();
        var interno = new ClienteRoteirizado(async token =>
        {
            await cancelamento.CancelAsync();
            token.ThrowIfCancellationRequested();
            return await Ok(token);
        }, Ok);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Criar(interno).GetResponseAsync(_mensagens, cancellationToken: cancelamento.Token));

        interno.Chamadas.ShouldBe(1);
    }

    [Fact]
    public async Task Responder_ComOFakeEmRateLimit_EsgotaAsTentativasConfiguradas()
    {
        var erro = await Should.ThrowAsync<ProvedorIndisponivelException>(() =>
            Criar(new FakeChatClient(ModoFake.RateLimit), maxRetries: 3).GetResponseAsync(_mensagens, cancellationToken: Ct));

        erro.Tipo.ShouldBe("rate_limit");
        _esperas.Count.ShouldBe(3);
    }

    // ---------- Cálculo do backoff ----------

    [Theory]
    [InlineData(1, 0.5, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 2, 4)]
    [InlineData(10, 15, 30)]
    public void CalcularEspera_SemRetryAfter_ExponencialComJitterELimite(int tentativa, double minimo, double maximo)
    {
        var aleatorio = new Random(1);
        for (var i = 0; i < 50; i++)
        {
            ResilienciaChatClient.CalcularEspera(tentativa, null, aleatorio)
                .TotalSeconds.ShouldBeInRange(minimo, maximo);
        }
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(300, 60)]
    public void CalcularEspera_ComRetryAfter_RespeitaOPedidoAteOLimite(int retryAfter, int esperado)
    {
        ResilienciaChatClient.CalcularEspera(1, TimeSpan.FromSeconds(retryAfter), new Random(1))
            .ShouldBe(TimeSpan.FromSeconds(esperado));
    }

    // ---------- Apoio ----------

    private static Task<ChatResponse> Ok(CancellationToken _) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

    private static Func<CancellationToken, Task<ChatResponse>> RateLimit(TimeSpan? retryAfter) =>
        _ => throw new ProvedorIndisponivelException(ProvedorIndisponivelException.TipoRateLimit, "429", retryAfter);

    private static async Task<ChatResponse> Lento(CancellationToken token)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), token);
        return await Ok(token);
    }

    private static ClientResultException Http(int status, string? retryAfter = null) =>
        new(new RespostaHttpFalsa(status, retryAfter));

    /// <summary>Executa um passo por chamada, na ordem, e conta as chamadas.</summary>
    private sealed class ClienteRoteirizado(params Func<CancellationToken, Task<ChatResponse>>[] passos) : IChatClient
    {
        public int Chamadas { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            passos[Chamadas++](cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RespostaHttpFalsa(int status, string? retryAfter) : PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => "Teste";

        public override Stream? ContentStream { get; set; } = new MemoryStream();

        public override BinaryData Content => BinaryData.Empty;

        protected override PipelineResponseHeaders HeadersCore => new Cabecalhos(retryAfter);

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BinaryData.Empty);

        public override void Dispose()
        {
        }
    }

    private sealed class Cabecalhos(string? retryAfter) : PipelineResponseHeaders
    {
        public override bool TryGetValue(string name, [NotNullWhen(true)] out string? value)
        {
            value = name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) ? retryAfter : null;
            return value is not null;
        }

        public override bool TryGetValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = TryGetValue(name, out var valor) ? [valor] : null;
            return values is not null;
        }

        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            (retryAfter is null ? [] : new[] { KeyValuePair.Create("Retry-After", retryAfter) }).AsEnumerable()
                .GetEnumerator();
    }
}
