using System.Net;
using System.Text.Json;
using HelpDesk.IntegrationTests.Infraestrutura;

namespace HelpDesk.IntegrationTests.Api;

public sealed class CategoriasTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListarCategorias_BancoComSeed_RetornaAsCincoOrdenadasPorNomeNoFormatoDoContrato()
    {
        using var resposta = await api.CriarClienteAtendente().GetAsync("/api/categorias", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        var categorias = json.RootElement.EnumerateArray().ToList();

        categorias.Select(c => c.GetProperty("nome").GetString())
            .ShouldBe(["Acesso/Login", "Bug no sistema", "Dúvida", "Financeiro", "Infraestrutura"]);
        categorias.ShouldAllBe(c => c.GetProperty("id").GetInt16() > 0);
        categorias[0].EnumerateObject().Select(p => p.Name).ShouldBe(["id", "nome"]);
    }
}
