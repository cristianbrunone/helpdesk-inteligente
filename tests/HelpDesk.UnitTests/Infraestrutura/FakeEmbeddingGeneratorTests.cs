using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Ia.Fake;
using Microsoft.Extensions.AI;

namespace HelpDesk.UnitTests.Infraestrutura;

public sealed class FakeEmbeddingGeneratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Gerar_QualquerTexto_Devolve768DimensoesComNormaUm()
    {
        var vetores = await GerarAsync("Erro 403 ao abrir o módulo de boletos", "", "!!! ???");

        vetores.ShouldAllBe(v => v.Length == 768);
        vetores.ShouldAllBe(v => Math.Abs(Norma(v) - 1) < 1e-5);
    }

    [Fact]
    public async Task Gerar_MesmoTextoEmInstanciasDiferentes_DevolveOMesmoVetor()
    {
        var a = (await GerarAsync("Boleto com valor divergente"))[0];
        var b = (await GerarAsync("Boleto com valor divergente"))[0];

        b.ShouldBe(a);
    }

    [Fact]
    public async Task Gerar_TextosComTermosEmComum_FicamMaisProximosQueTextosSemRelacao()
    {
        var vetores = await GerarAsync(
            "Erro 403 em boletos",
            "Erro 403 ao abrir o módulo de boletos no portal financeiro",
            "Impressora da recepção não imprime");

        var parecido = Cosseno(vetores[0], vetores[1]);
        var diferente = Cosseno(vetores[0], vetores[2]);

        parecido.ShouldBeGreaterThan(0.5);
        diferente.ShouldBeLessThan(0.1);
    }

    [Fact]
    public async Task Gerar_AcentoCaixaEPlural_NaoMudamOVetor()
    {
        var vetores = await GerarAsync("Configuração do BOLETO", "configuracao dos boletos");

        Cosseno(vetores[0], vetores[1]).ShouldBe(1, tolerance: 1e-5);
    }

    [Fact]
    public async Task Gerar_Lote_InformaOModeloEOsTokensParaATelemetria()
    {
        using var gerador = new FakeEmbeddingGenerator();

        var resultado = await gerador.GenerateAsync(["erro 403 boletos", "vpn caindo"], cancellationToken: Ct);

        resultado.Usage!.InputTokenCount.ShouldBe(5);
        resultado.ShouldAllBe(e => e.ModelId == OpcoesLlm.ModeloEmbeddingFake);
        gerador.GetService<EmbeddingGeneratorMetadata>()!.DefaultModelDimensions.ShouldBe(768);
    }

    [Fact]
    public void Termos_TextoComStopwordsEPontuacao_FicaSoComTermosDeConteudo()
    {
        FakeEmbeddingGenerator.Termos("Não consigo emitir a nota fiscal: erro E1043!")
            .ShouldBe(["consigo", "emitir", "nota", "fiscal", "erro", "e1043"]);
    }

    private static async Task<float[][]> GerarAsync(params string[] textos)
    {
        using var gerador = new FakeEmbeddingGenerator();
        var resultado = await gerador.GenerateAsync(textos, cancellationToken: Ct);
        return [.. resultado.Select(e => e.Vector.ToArray())];
    }

    private static double Norma(float[] v) => Math.Sqrt(v.Sum(x => (double)x * x));

    private static double Cosseno(float[] a, float[] b) => a.Zip(b, (x, y) => (double)x * y).Sum();
}
