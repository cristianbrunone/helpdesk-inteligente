using System.Net;
using System.Text.Json;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.Extensions.Logging;

namespace HelpDesk.IntegrationTests.Api;

public sealed class SaudeTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_BancoDisponivel_RetornaHealthyComCheckDoBanco()
    {
        using var resposta = await api.CreateClient().GetAsync("/health", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("status").GetString().ShouldBe("Healthy");
        var banco = json.RootElement.GetProperty("checks").GetProperty("banco");
        banco.GetProperty("status").GetString().ShouldBe("Healthy");
        banco.GetProperty("duracaoMs").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Health_BancoDisponivel_NaoRegistraLogDaRequisicaoEmInformation()
    {
        var id = $"health-{Guid.NewGuid():N}";
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/health");
        requisicao.Headers.Add("X-Correlation-Id", id);

        using var resposta = await api.CreateClient().SendAsync(requisicao, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        api.Logs.Registros
            .Where(r => r.Nivel >= LogLevel.Information && Equals(r.Escopo.GetValueOrDefault("CorrelationId"), id))
            .ShouldBeEmpty();
    }
}

public sealed class SaudeSemBancoTests(ApiSemBancoFactory api) : IClassFixture<ApiSemBancoFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_BancoIndisponivel_Retorna503Unhealthy()
    {
        using var resposta = await api.CreateClient().GetAsync("/health", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("status").GetString().ShouldBe("Unhealthy");
        json.RootElement.GetProperty("checks").GetProperty("banco").GetProperty("status").GetString()
            .ShouldBe("Unhealthy");
    }
}
