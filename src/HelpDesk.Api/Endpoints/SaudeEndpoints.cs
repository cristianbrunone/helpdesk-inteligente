using HelpDesk.Api.Saude;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HelpDesk.Api.Endpoints;

/// <summary>
/// <c>GET /health</c> no formato do contrato. O provedor de LLM fica de fora de propósito (a API funciona sem ele);
/// o check <c>filaTriagem</c> entra na Sprint 2, junto com a tabela <c>triagens_ia</c>.
/// </summary>
internal static class SaudeEndpoints
{
    public const string Rota = "/health";

    public static IServiceCollection AdicionarSaude(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<BancoHealthCheck>("banco", HealthStatus.Unhealthy, timeout: TimeSpan.FromSeconds(3));
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
            checks = relatorio.Entries.ToDictionary(
                entrada => entrada.Key,
                entrada => new
                {
                    status = entrada.Value.Status.ToString(),
                    duracaoMs = (int)Math.Round(entrada.Value.Duration.TotalMilliseconds),
                }),
        }, http.RequestAborted);
}
