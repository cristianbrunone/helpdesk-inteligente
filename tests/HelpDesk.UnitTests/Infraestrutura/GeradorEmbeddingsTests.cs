using HelpDesk.Application.Triagem;
using HelpDesk.Infrastructure.Ia;
using Microsoft.Extensions.AI;

namespace HelpDesk.UnitTests.Infraestrutura;

public sealed class GeradorEmbeddingsTests
{
    private static readonly MascaradorDadosPessoais _mascarador = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Gerar_ProvedorDevolveVetorNaoNormalizado_NormalizaParaNormaUm()
    {
        // PoC (ADR-0011): o Gemini devolve o vetor reduzido para 768 com norma ≈ 0,588.
        var vetor = Enumerable.Repeat(0.588f / MathF.Sqrt(768), 768).ToArray();
        var interno = new GeradorRoteirizado(_ => [vetor]);

        var resultado = await Criar(interno).GerarAsync([_mascarador.Mascarar("erro 403")], Ct);

        Math.Sqrt(resultado[0].Sum(x => (double)x * x)).ShouldBe(1, tolerance: 1e-5);
    }

    [Fact]
    public async Task Gerar_Sempre_PedeAs768DimensoesEEnviaSoOTextoMascarado()
    {
        var interno = new GeradorRoteirizado(textos => [.. textos.Select(_ => Unitario())]);
        var texto = _mascarador.Mascarar("Ligue para Maria no (11) 98765-4321", ["Maria"]);

        await Criar(interno).GerarAsync([texto], Ct);

        interno.Opcoes!.Dimensions.ShouldBe(768);
        var enviado = interno.Textos.ShouldHaveSingleItem();
        enviado.ShouldNotContain("Maria");
        enviado.ShouldNotContain("98765");
    }

    [Theory]
    [InlineData(767)]
    [InlineData(3072)]
    public async Task Gerar_ProvedorIgnoraADimensao_FalhaComErroDeConfiguracao(int dimensoes)
    {
        var interno = new GeradorRoteirizado(_ => [new float[dimensoes]]);

        var erro = await Should.ThrowAsync<InvalidOperationException>(() =>
            Criar(interno).GerarAsync([_mascarador.Mascarar("erro")], Ct));

        erro.Message.ShouldContain($"{dimensoes} dimensões");
    }

    [Fact]
    public async Task Gerar_ProvedorDevolveMenosVetoresQueTextos_Falha()
    {
        var interno = new GeradorRoteirizado(_ => [Unitario()]);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            Criar(interno).GerarAsync([_mascarador.Mascarar("um"), _mascarador.Mascarar("dois")], Ct));
    }

    [Fact]
    public async Task Gerar_ListaVazia_NaoChamaOProvedor()
    {
        var interno = new GeradorRoteirizado(_ => throw new InvalidOperationException("não devia chamar"));

        (await Criar(interno).GerarAsync([], Ct)).ShouldBeEmpty();
    }

    [Fact]
    public void Modelo_ProvedorReal_EOModeloDeEmbeddingConfigurado()
    {
        var opcoes = Opcoes(TipoProvedorLlm.OpenAiCompativel);
        new GeradorEmbeddings(new GeradorRoteirizado(_ => []), opcoes).Modelo.ShouldBe("text-embedding-teste");
        new GeradorEmbeddings(new GeradorRoteirizado(_ => []), Opcoes(TipoProvedorLlm.Fake)).Modelo
            .ShouldBe(OpcoesLlm.ModeloEmbeddingFake);
    }

    private static GeradorEmbeddings Criar(GeradorRoteirizado interno) =>
        new(interno, Opcoes(TipoProvedorLlm.OpenAiCompativel));

    private static OpcoesLlm Opcoes(TipoProvedorLlm provedor) => new()
    {
        Provedor = provedor,
        ModeloChat = "x",
        ModeloEmbedding = "text-embedding-teste",
        Timeout = TimeSpan.FromSeconds(5),
        MaxRetries = 0,
        MaxTokensSaidaTriagem = 800,
    };

    private static float[] Unitario()
    {
        var v = new float[768];
        v[0] = 1;
        return v;
    }

    /// <summary>Gerador de teste: devolve o que o roteiro mandar e guarda o que recebeu.</summary>
    private sealed class GeradorRoteirizado(Func<IReadOnlyList<string>, float[][]> roteiro)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<string> Textos { get; } = [];

        public EmbeddingGenerationOptions? Opcoes { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Textos.AddRange(values);
            Opcoes = options;
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                roteiro(Textos).Select(v => new Embedding<float>(v))));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
