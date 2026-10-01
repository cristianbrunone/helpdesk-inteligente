using HelpDesk.Api.Saude;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HelpDesk.Api.Endpoints;

/// <summary>
/// <c>GET /health</c> no formato do contrato: <c>banco</c> (indisponível → 503) e <c>filaTriagem</c> (atrasada →
/// <c>Degraded</c>, 200). O provedor de LLM fica de fora de propósito: a API funciona sem ele.
/// </summary>
internal static class SaudeEndpoints
{
    public const string Rota = "/health";

    public static IServiceCollection AdicionarSaude(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<BancoHealthCheck>("banco", HealthStatus.Unhealthy, timeout: TimeSpan.FromSeconds(3))
            .AddCheck<FilaTriagemHealthCheck>("filaTriagem", HealthStatus.Degraded, timeout: TimeSpan.FromSeconds(3));
        return services;
    }

    public static IEndpointRouteBuilder MapSaude(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks(Rota, new HealthCheckOptions { ResponseWriter = EscreverAsync })
            .WithTags("Saúde");
        return app;
    }

    private static Task EscreverAsync(HttpContext http, HealthReport relatorio) =>
        http.Response.WriteAsJsonAsync(new
        {
            status = relatorio.Status.ToString(),
            checks = relatorio.Entries.ToDictionary(entrada => entrada.Key, entrada => Check(entrada.Value)),
        }, http.RequestAborted);

    /// <summary>Status, duração, os dados do check (ex.: <c>pendentes</c>) e o motivo, quando houver.</summary>
    private static Dictionary<string, object?> Check(HealthReportEntry entrada)
    {
        var check = new Dictionary<string, object?>
        {
            ["status"] = entrada.Status.ToString(),
            ["duracaoMs"] = (int)Math.Round(entrada.Duration.TotalMilliseconds),
        };
        foreach (var (chave, valor) in entrada.Data)
        {
            check[chave] = valor;
        }

        if (entrada.Description is { } motivo)
        {
            check["motivo"] = motivo;
        }

        return check;
    }
}
