using System.Runtime.CompilerServices;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.UnitTests.Infraestrutura;

/// <summary>Resiliência e telemetria no streaming do copiloto (ADR-0012): sem rede, com provedores roteirizados.</summary>
public sealed class StreamingChatClientTests
{
    private static readonly ChatMessage[] _mensagens = [new(ChatRole.User, "oi")];
    private static readonly Guid _chamadoId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<TimeSpan> _esperas = [];

    // ---------- Resiliência ----------

    [Fact]
    public async Task Resiliencia_StreamSaudavel_EntregaTodosOsPedacosNumaTentativa()
    {
        var provedor = new ProvedorStream(Pedacos("Olá", ", ", "atendente"));

        var texto = await LerAsync(Resiliencia(provedor));

        texto.ShouldBe("Olá, atendente");
        provedor.Chamadas.ShouldBe(1);
        _esperas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Resiliencia_RateLimitAntesDoPrimeiroPedaco_RepeteEEntregaUmaVezSo()
    {
        var provedor = new ProvedorStream(Falha(RateLimit()), Pedacos("a", "b"));

        var texto = await LerAsync(Resiliencia(provedor));

        texto.ShouldBe("ab");
        provedor.Chamadas.ShouldBe(2);
        _esperas.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Resiliencia_FalhaDepoisDoPrimeiroPedaco_SobeClassificadaSemRepetir()
    {
        var provedor = new ProvedorStream(Pedacos(["a"], falhaNoFim: RateLimit()), Pedacos("nunca"));
        var recebidos = new List<string>();

        var erro = await Should.ThrowAsync<ProvedorIndisponivelException>(async () =>
        {
            await foreach (var atualizacao in Resiliencia(provedor).GetStreamingResponseAsync(_mensagens, null, Ct))
            {
                recebidos.Add(atualizacao.Text);
            }
        });

        erro.Tipo.ShouldBe("rate_limit");
        recebidos.ShouldBe(["a"]); // repetir duplicaria o "a" na tela
        provedor.Chamadas.ShouldBe(1);
    }

    [Fact]
    public async Task Resiliencia_ProvedorMudoEmTodasAsTentativas_EsgotaComTimeout()
    {
        var provedor = new ProvedorStream(Mudo, Mudo);

        var erro = await Should.ThrowAsync<ProvedorIndisponivelException>(() =>
            LerAsync(Resiliencia(provedor, maxRetries: 1, timeout: TimeSpan.FromMilliseconds(50))));

        erro.Tipo.ShouldBe("timeout");
        provedor.Chamadas.ShouldBe(2);
    }

    [Fact]
    public async Task Resiliencia_PedacosEspacadosMenosQueOPrazo_StreamLongoNaoECortado()
    {
        // 4 pedaços a cada 100 ms (400 ms no total) com prazo de 250 ms: o prazo é de inatividade, não do stream.
        var provedor = new ProvedorStream(Pedacos(["1", "2", "3", "4"], intervalo: TimeSpan.FromMilliseconds(100)));

        var texto = await LerAsync(Resiliencia(provedor, timeout: TimeSpan.FromMilliseconds(250)));

        texto.ShouldBe("1234");
    }

    [Fact]
    public async Task Resiliencia_CancelamentoDeFora_SobeSemNovasTentativas()
    {
        using var cancelamento = new CancellationTokenSource();
        var provedor = new ProvedorStream(Mudo, Pedacos("nunca"));
        cancelamento.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            LerAsync(Resiliencia(provedor), cancelamento.Token));

        provedor.Chamadas.ShouldBe(1);
        _esperas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Resiliencia_OpcoesDeQuemChama_NaoSaoAlteradasEOProvedorRecebeOPrazo()
    {
        var provedor = new ProvedorStream(Pedacos("ok"));
        var opcoes = new ChatOptions().ParaCopiloto(_chamadoId);

        await LerAsync(Resiliencia(provedor), opcoes: opcoes);

        opcoes.AdditionalProperties!.ContainsKey(ContextoUsoLlm.Prazo).ShouldBeFalse();
        provedor.Opcoes!.AdditionalProperties![ContextoUsoLlm.Prazo].ShouldBeOfType<CancellationToken>();
        provedor.Opcoes.AdditionalProperties[ContextoUsoLlm.ChamadoId].ShouldBe(_chamadoId);
    }

    // ---------- Telemetria ----------

    [Fact]
    public async Task Telemetria_StreamCompleto_UmRegistroDoCopilotoComTokensEModelo()
    {
        var registro = new RegistroEmMemoria();
        var provedor = new ProvedorStream(Pedacos(["Olá"], uso: new UsageDetails { InputTokenCount = 120, OutputTokenCount = 9 }));

        await LerAsync(Telemetria(provedor, registro), opcoes: new ChatOptions().ParaCopiloto(_chamadoId));

        var uso = registro.Registros.ShouldHaveSingleItem();
        (uso.Operacao, uso.ChamadoId, uso.TriagemId).ShouldBe(("copiloto", _chamadoId, (Guid?)null));
        (uso.Modelo, uso.TokensEntrada, uso.TokensSaida).ShouldBe(("modelo-stream", 120, 9));
        (uso.Sucesso, uso.ErroTipo).ShouldBe((true, null));
    }

    [Fact]
    public async Task Telemetria_FalhaNoMeioDoStream_RegistraOTipoDoErro()
    {
        var registro = new RegistroEmMemoria();
        var provedor = new ProvedorStream(Pedacos(["a"], falhaNoFim: RateLimit()));

        await Should.ThrowAsync<ProvedorIndisponivelException>(() =>
            LerAsync(Telemetria(provedor, registro), opcoes: new ChatOptions().ParaCopiloto(_chamadoId)));

        var uso = registro.Registros.ShouldHaveSingleItem();
        (uso.Sucesso, uso.ErroTipo).ShouldBe((false, "rate_limit"));
    }

    [Fact]
    public async Task Telemetria_ConsumidorParaDeLer_RegistraCancelado()
    {
        var registro = new RegistroEmMemoria();
        var provedor = new ProvedorStream(Pedacos("a", "b", "c"));

        await foreach (var _ in Telemetria(provedor, registro)
            .GetStreamingResponseAsync(_mensagens, new ChatOptions().ParaCopiloto(_chamadoId), Ct))
        {
            break; // o atendente fechou o painel
        }

        registro.Registros.ShouldHaveSingleItem().ErroTipo.ShouldBe(TelemetriaChatClient.TipoCancelado);
    }

    [Fact]
    public async Task Telemetria_PrazoDaResilienciaEsgotado_RegistraTimeoutENaoCancelado()
    {
        var registro = new RegistroEmMemoria();
        var cliente = new ResilienciaChatClient(Telemetria(new ProvedorStream(Mudo), registro), Opcoes(0,
            TimeSpan.FromMilliseconds(50)), NullLogger<ResilienciaChatClient>.Instance);

        await Should.ThrowAsync<ProvedorIndisponivelException>(() =>
            LerAsync(cliente, opcoes: new ChatOptions().ParaCopiloto(_chamadoId)));

        registro.Registros.ShouldHaveSingleItem().ErroTipo.ShouldBe("timeout");
    }

    // ---------- Apoio ----------

    private ResilienciaChatClient Resiliencia(IChatClient interno, int maxRetries = 3, TimeSpan? timeout = null) =>
        new(interno, Opcoes(maxRetries, timeout ?? TimeSpan.FromSeconds(5)), NullLogger<ResilienciaChatClient>.Instance,
            esperar: (espera, _) =>
            {
                _esperas.Add(espera);
                return Task.CompletedTask;
            },
            aleatorio: new Random(7));

    private static TelemetriaChatClient Telemetria(IChatClient interno, RegistroEmMemoria registro) =>
        new(interno, Opcoes(0, TimeSpan.FromSeconds(5)), registro, NullLogger<TelemetriaChatClient>.Instance);

    private static OpcoesLlm Opcoes(int maxRetries, TimeSpan timeout) => new()
    {
        Provedor = TipoProvedorLlm.Fake,
        ModeloChat = "x",
        Timeout = timeout,
        MaxRetries = maxRetries,
        MaxTokensSaidaTriagem = 800,
    };

    private static async Task<string> LerAsync(
        IChatClient cliente, CancellationToken? token = null, ChatOptions? opcoes = null)
    {
        var texto = new System.Text.StringBuilder();
        await foreach (var atualizacao in cliente.GetStreamingResponseAsync(_mensagens, opcoes, token ?? Ct))
        {
            texto.Append(atualizacao.Text);
        }

        return texto.ToString();
    }

    private static ProvedorIndisponivelException RateLimit() =>
        new(ProvedorIndisponivelException.TipoRateLimit, "429");

    private static Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> Pedacos(params string[] textos) =>
        Pedacos(textos, null);

    private static Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> Pedacos(
        string[] textos, Exception? falhaNoFim = null, UsageDetails? uso = null, TimeSpan? intervalo = null) =>
        token => Gerar(textos, falhaNoFim, uso, intervalo, token);

    private static async IAsyncEnumerable<ChatResponseUpdate> Gerar(
        string[] textos, Exception? falhaNoFim, UsageDetails? uso, TimeSpan? intervalo,
        [EnumeratorCancellation] CancellationToken token)
    {
        foreach (var texto in textos)
        {
            if (intervalo is { } espera)
            {
                await Task.Delay(espera, token);
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, texto) { ModelId = "modelo-stream" };
        }

        if (falhaNoFim is not null)
        {
            throw falhaNoFim;
        }

        if (uso is not null)
        {
            yield return new ChatResponseUpdate { Contents = [new UsageContent(uso)] };
        }
    }

    private static Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> Falha(Exception erro) =>
        _ => Lancar(erro);

#pragma warning disable CS1998 // iterador assíncrono que só lança
    private static async IAsyncEnumerable<ChatResponseUpdate> Lancar(Exception erro)
    {
        throw erro;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
#pragma warning restore CS1998

    private static async IAsyncEnumerable<ChatResponseUpdate> Mudo([EnumeratorCancellation] CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        yield break;
    }

    /// <summary>Um stream por chamada, na ordem; guarda as opções recebidas.</summary>
    private sealed class ProvedorStream(params Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>[] streams)
        : IChatClient
    {
        public int Chamadas { get; private set; }

        public ChatOptions? Opcoes { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Opcoes = options;
            return streams[Chamadas++](cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RegistroEmMemoria : IRegistroUsoLlm
    {
        public List<RegistroUsoLlm> Registros { get; } = [];

        public Task RegistrarAsync(RegistroUsoLlm registro)
        {
            Registros.Add(registro);
            return Task.CompletedTask;
        }
    }
}
