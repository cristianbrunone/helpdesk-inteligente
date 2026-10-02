using System.Net;
using System.Text.Json;
using HelpDesk.IntegrationTests.Infraestrutura;

namespace HelpDesk.IntegrationTests.Api;

/// <summary>O endpoint do dashboard no formato do contrato (os números são conferidos em ConsultaDashboardTests).</summary>
public sealed class DashboardTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resumo_BancoComSeed_RetornaOFormatoDoContrato()
    {
        using var resposta = await api.CriarClienteAtendente().GetAsync("/api/dashboard/resumo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        var raiz = json.RootElement;

        raiz.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["totalChamados", "porStatus", "porPrioridade", "tempoMedioResolucaoPorCategoria", "ia"]);
        raiz.GetProperty("totalChamados").GetInt32().ShouldBeGreaterThanOrEqualTo(200);
        raiz.GetProperty("porStatus").EnumerateArray().Select(s => s.GetProperty("status").GetString())
            .ShouldBe(["Aberto", "EmAndamento", "Resolvido", "Fechado", "Cancelado"]);
        raiz.GetProperty("porPrioridade").EnumerateArray().Select(p => p.GetProperty("prioridade").GetString())
            .ShouldBe(["Baixa", "Media", "Alta", "Critica"]);
        raiz.GetProperty("tempoMedioResolucaoPorCategoria")[0].EnumerateObject().Select(p => p.Name)
            .ShouldBe(["categoriaId", "categoria", "resolvidos", "tempoMedioHoras"]);

        var ia = raiz.GetProperty("ia");
        ia.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["taxaAceitacao", "aceitas", "rejeitadas", "pendentes", "falhas", "porCategoria", "consumo30d"]);
        // O seed tem triagens decididas (Sprint 3): a taxa existe e fica entre 0 e 1.
        ia.GetProperty("taxaAceitacao").GetDecimal().ShouldBeInRange(0m, 1m);
        ia.GetProperty("porCategoria").GetArrayLength().ShouldBeGreaterThan(0);
    }
}
