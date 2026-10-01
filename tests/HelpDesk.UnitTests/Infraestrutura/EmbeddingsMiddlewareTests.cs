using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.UnitTests.Infraestrutura;

/// <summary>Resiliência e telemetria do gerador de embeddings: as mesmas regras do chat.</summary>
public sealed class EmbeddingsMiddlewareTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly OpcoesLlm _opcoes = new()
    {
        Provedor = TipoProvedorLlm.OpenAiCompativel,
        ModeloChat = "x",
        ModeloEmbedding = "gemini-embedding-001",
        Timeout = TimeSpan.FromSeconds(5),
        MaxRetries = 2,
        MaxTokensSaidaTriagem = 800,
    };

    private readonly List<TimeSpan> _esperas = [];

    [Fact]
    public async Task Resiliencia_RateLimitDepoisOk_RepeteComBackoff()
    {
        var interno = new GeradorRoteirizado(RateLimit, Ok);

        var resultado = await Resiliente(interno).GenerateAsync(["texto"], cancellationToken: Ct);

        resultado.ShouldHaveSingleItem();
        interno.Chamadas.ShouldBe(2);
        _esperas.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Resiliencia_FalhaTransitoriaEmTodasAsTentativas_LancaProvedorIndisponivel()
    {
        var interno = new GeradorRoteirizado(RateLimit, RateLimit, RateLimit);

        var erro = await Should.ThrowAsync<ProvedorIndisponivelException>(() =>
            Resiliente(interno).GenerateAsync(["texto"], cancellationToken: Ct));

        erro.Tipo.ShouldBe(ProvedorIndisponivelException.TipoRateLimit);
        interno.Chamadas.ShouldBe(3); // 1 + MaxRetries
    }

    [Fact]
    public async Task Resiliencia_ErroDefinitivo_SobeSemRepetir()
    {
        var interno = new GeradorRoteirizado(_ => throw new InvalidOperationException("400"), Ok);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Resiliente(interno).GenerateAsync(["texto"], cancellationToken: Ct));
        interno.Chamadas.ShouldBe(1);
    }

    [Fact]
    public async Task Telemetria_Sucesso_RegistraOperacaoEmbeddingComTokensESemChamado()
    {
        var registro = new RegistroEmMemoria();
        var gerador = new TelemetriaEmbeddingGenerator(new GeradorRoteirizado(Ok), _opcoes, registro,
            NullLogger<TelemetriaEmbeddingGenerator>.Instance);

        await gerador.GenerateAsync(["um", "dois"], cancellationToken: Ct);

        var uso = registro.Registros.ShouldHaveSingleItem();
        uso.Operacao.ShouldBe(RegistroUsoLlm.OperacaoEmbedding);
        uso.Modelo.ShouldBe("gemini-embedding-001");
        uso.Provedor.ShouldBe("openai-compatible");
        uso.TokensEntrada.ShouldBe(7);
        uso.Sucesso.ShouldBeTrue();
        uso.TriagemId.ShouldBeNull();
        uso.ChamadoId.ShouldBeNull();
    }

    [Fact]
    public async Task Telemetria_Falha_RegistraOTipoDoErroERelanca()
    {
        var registro = new RegistroEmMemoria();
        var gerador = new TelemetriaEmbeddingGenerator(new GeradorRoteirizado(RateLimit), _opcoes, registro,
            NullLogger<TelemetriaEmbeddingGenerator>.Instance);

        await Should.ThrowAsync<ProvedorIndisponivelException>(() =>
            gerador.GenerateAsync(["um"], cancellationToken: Ct));

        var uso = registro.Registros.ShouldHaveSingleItem();
        uso.Sucesso.ShouldBeFalse();
        uso.ErroTipo.ShouldBe(ProvedorIndisponivelException.TipoRateLimit);
    }

    private ResilienciaEmbeddingGenerator Resiliente(GeradorRoteirizado interno) =>
        new(interno, _opcoes, NullLogger<ResilienciaEmbeddingGenerator>.Instance,
            esperar: (espera, _) =>
            {
                _esperas.Add(espera);
                return Task.CompletedTask;
            },
            aleatorio: new Random(7));

    private static GeneratedEmbeddings<Embedding<float>> Ok(IList<string> textos) =>
        new(textos.Select(_ => new Embedding<float>(new float[768])))
        {
            Usage = new UsageDetails { InputTokenCount = 7 },
        };

    private static GeneratedEmbeddings<Embedding<float>> RateLimit(IList<string> textos) =>
        throw new ProvedorIndisponivelException(ProvedorIndisponivelException.TipoRateLimit, "429");

    private sealed class GeradorRoteirizado(params Func<IList<string>, GeneratedEmbeddings<Embedding<float>>>[] passos)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int Chamadas { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var passo = passos[Math.Min(Chamadas++, passos.Length - 1)];
            return Task.FromResult(passo([.. values]));
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
