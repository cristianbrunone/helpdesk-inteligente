using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HelpDesk.Api.Saude;

/// <summary>Abre uma conexão com o banco. Banco indisponível → <c>Unhealthy</c> → 503 (contrato: <c>/health</c>).</summary>
internal sealed class BancoHealthCheck(HelpDeskDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "Banco de dados indisponível.");
}
