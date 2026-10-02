using System.Net;
using System.Text;
using System.Text.Json;
using HelpDesk.Infrastructure.Persistencia.Seed;
using HelpDesk.IntegrationTests.Infraestrutura;

namespace HelpDesk.IntegrationTests.Api;

/// <summary>
/// Quem pode o quê (ADR-0026, contrato §3 "Perfis e acesso"). A autorização roda antes do caso de uso, então um id
/// qualquer basta: o que se verifica aqui é o 401/403, e não a regra de negócio do endpoint.
/// </summary>
public sealed class AutorizacaoTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly string _id = Guid.CreateVersion7().ToString();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> EndpointsProtegidos() => new()
    {
        { "GET", "/api/categorias" },
        { "GET", "/api/config/ia" },
        { "GET", "/api/chamados" },
        { "GET", $"/api/chamados/{_id}" },
        { "POST", "/api/chamados" },
        { "PATCH", $"/api/chamados/{_id}/status" },
        { "POST", $"/api/chamados/{_id}/comentarios" },
        { "POST", $"/api/chamados/{_id}/triagem" },
        { "POST", $"/api/chamados/{_id}/triagem/aceitar" },
        { "POST", $"/api/chamados/{_id}/triagem/rejeitar" },
        { "POST", $"/api/chamados/{_id}/copiloto" },
        { "GET", "/api/dashboard/resumo" },
        { "GET", "/api/auth/eu" },
    };

    public static TheoryData<string, string> EndpointsDoAtendente() => new()
    {
        { "PATCH", $"/api/chamados/{_id}/status" },
        { "POST", $"/api/chamados/{_id}/triagem" },
        { "POST", $"/api/chamados/{_id}/triagem/aceitar" },
        { "POST", $"/api/chamados/{_id}/triagem/rejeitar" },
        { "POST", $"/api/chamados/{_id}/copiloto" },
        { "GET", "/api/dashboard/resumo" },
    };

    [Theory]
    [MemberData(nameof(EndpointsProtegidos))]
    public async Task SemSessao_EndpointProtegido_401NaoAutenticado(string metodo, string rota)
    {
        using var resposta = await api.CreateClient().SendAsync(Requisicao(metodo, rota), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodigoAsync(resposta)).ShouldBe("nao_autenticado");
    }

    [Theory]
    [MemberData(nameof(EndpointsDoAtendente))]
    public async Task Solicitante_EndpointDoAtendente_403AcessoNegado(string metodo, string rota)
    {
        using var resposta = await api.CriarCliente(GeradorSeedUsuarios.MarinaSolicitante)
            .SendAsync(Requisicao(metodo, rota), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodigoAsync(resposta)).ShouldBe("acesso_negado");
    }

    [Theory]
    [InlineData("/api/categorias")]
    [InlineData("/api/config/ia")]
    [InlineData("/api/chamados")]
    [InlineData("/api/auth/eu")]
    public async Task Solicitante_LeituraComum_200(string rota)
    {
        using var resposta = await api.CriarCliente(GeradorSeedUsuarios.PauloSolicitante).GetAsync(rota, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Atendente_Dashboard_200()
    {
        using var resposta = await api.CriarClienteAtendente().GetAsync("/api/dashboard/resumo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET", "/health", HttpStatusCode.OK)]
    [InlineData("GET", "/openapi/v1.json", HttpStatusCode.OK)]
    [InlineData("GET", "/swagger/index.html", HttpStatusCode.OK)]
    [InlineData("POST", "/api/auth/sair", HttpStatusCode.NoContent)]
    public async Task SemSessao_RotasPublicas_Respondem(string metodo, string rota, HttpStatusCode esperado)
    {
        using var resposta = await api.CreateClient().SendAsync(Requisicao(metodo, rota), Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    private static HttpRequestMessage Requisicao(string metodo, string rota) =>
        new(new HttpMethod(metodo), rota)
        {
            Content = metodo == "GET" ? null : new StringContent("{}", Encoding.UTF8, "application/json"),
        };

    private static async Task<string?> CodigoAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("codigo").GetString();
    }
}
