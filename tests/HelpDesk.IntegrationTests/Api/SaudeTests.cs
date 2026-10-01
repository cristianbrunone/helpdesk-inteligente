using System.Net;
using System.Text.Json;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.IntegrationTests.Api;

/// <summary>
/// O <c>/health</c> roda em banco isolado: no banco compartilhado, outros testes deixam triagens pendentes antigas,
/// e o check <c>filaTriagem</c> ficaria <c>Degraded</c> por causa deles.
/// </summary>
public sealed class SaudeTests(ApiFactory api, BancoFixture banco) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Health_BancoDisponivelEFilaVazia_RetornaHealthyComOsDoisChecks()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await using var isolada = api.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", bancoIsolado));

        using var resposta = await isolada.CreateClient().GetAsync("/health", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("status").GetString().ShouldBe("Healthy");
        var checks = json.RootElement.GetProperty("checks");
        checks.GetProperty("banco").GetProperty("status").GetString().ShouldBe("Healthy");
        checks.GetProperty("banco").GetProperty("duracaoMs").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        var fila = checks.GetProperty("filaTriagem");
        fila.GetProperty("status").GetString().ShouldBe("Healthy");
        fila.GetProperty("pendentes").GetInt32().ShouldBe(0);
        fila.GetProperty("maisAntigaSegundos").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Health_PendenteHaMaisDe5Minutos_RetornaDegradedCom200EMotivo()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await CriarPendenteAsync(bancoIsolado, DateTimeOffset.UtcNow.AddMinutes(-10));
        await using var isolada = api.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", bancoIsolado));

        using var resposta = await isolada.CreateClient().GetAsync("/health", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("status").GetString().ShouldBe("Degraded");
        var fila = json.RootElement.GetProperty("checks").GetProperty("filaTriagem");
        fila.GetProperty("status").GetString().ShouldBe("Degraded");
        fila.GetProperty("pendentes").GetInt32().ShouldBe(1);
        fila.GetProperty("maisAntigaSegundos").GetInt32().ShouldBeGreaterThan(300);
        fila.GetProperty("motivo").GetString().ShouldBe("Há triagem pendente há mais de 5 minutos.");
    }

    [Fact]
    public async Task Health_TriagemDesativadaComPendentes_RetornaDegradedComOMotivo()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await CriarPendenteAsync(bancoIsolado, DateTimeOffset.UtcNow);
        await using var isolada = api.WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", bancoIsolado)
            .UseSetting("IA_TRIAGEM_HABILITADA", "false"));

        using var resposta = await isolada.CreateClient().GetAsync("/health", Ct);

        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("checks").GetProperty("filaTriagem").GetProperty("motivo").GetString()
            .ShouldBe("Triagem desativada com triagens pendentes na fila.");
        json.RootElement.GetProperty("status").GetString().ShouldBe("Degraded");
    }

    private async Task CriarPendenteAsync(string connectionString, DateTimeOffset criadoEm)
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo", "maria@example.com", null, null, criadoEm);
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Chamados.Add(chamado);
        db.Triagens.Add(TriagemIA.Criar(chamado, criadoEm));
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Health_BancoDisponivel_NaoRegistraLogDaRequisicaoEmInformation()
    {
        var id = $"health-{Guid.NewGuid():N}";
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/health");
        requisicao.Headers.Add("X-Correlation-Id", id);
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await using var isolada = api.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", bancoIsolado));

        using var resposta = await isolada.CreateClient().SendAsync(requisicao, Ct);

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
