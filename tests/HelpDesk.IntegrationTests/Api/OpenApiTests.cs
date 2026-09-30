using System.Text.Json;
using HelpDesk.IntegrationTests.Infraestrutura;

namespace HelpDesk.IntegrationTests.Api;

public sealed class OpenApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DocumentoOpenApi_Requisitado_RetornaJsonOpenApi()
    {
        using var resposta = await api.CreateClient().GetAsync("/openapi/v1.json", Ct);

        resposta.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("openapi").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task SwaggerUi_Requisitada_RetornaPaginaHtml()
    {
        using var resposta = await api.CreateClient().GetAsync("/swagger/index.html", Ct);

        resposta.EnsureSuccessStatusCode();
        resposta.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }
}
