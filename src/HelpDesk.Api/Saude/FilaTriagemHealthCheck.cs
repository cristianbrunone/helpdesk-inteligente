using HelpDesk.Application.Triagem;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HelpDesk.Api.Saude;

/// <summary>
/// Estado da fila de triagem (contrato: <c>/health</c>). Fica <c>Degraded</c> (HTTP 200) quando a pendente mais
/// antiga passa de 5 minutos, o que indica Worker parado ou provedor lento, ou quando a triagem está desligada com
/// pendentes acumuladas (ADR-0021). Nunca derruba a API: o provedor de LLM, de propósito, nem entra no health.
/// </summary>
internal sealed class FilaTriagemHealthCheck(HelpDeskDbContext db, OpcoesIA opcoesIA) : IHealthCheck
{
    public static readonly TimeSpan LimiteAtraso = TimeSpan.FromMinutes(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // O índice parcial da fila (#9) torna esta contagem barata mesmo com muitas triagens históricas.
        var estado = await db.Database.SqlQuery<EstadoFila>($"""
            SELECT count(*)::int AS "Pendentes",
                   extract(epoch FROM now() - min(criado_em))::int AS "MaisAntigaSegundos"
            FROM triagens_ia WHERE status = 'pendente'
            """).SingleAsync(cancellationToken);

        var dados = new Dictionary<string, object>
        {
            ["pendentes"] = estado.Pendentes,
            ["maisAntigaSegundos"] = estado.MaisAntigaSegundos!,
        };

        if (!opcoesIA.TriagemHabilitada)
        {
            return estado.Pendentes > 0
                ? HealthCheckResult.Degraded("Triagem desativada com triagens pendentes na fila.", data: dados)
                : HealthCheckResult.Healthy("Triagem desativada.", dados);
        }

        return estado.MaisAntigaSegundos > LimiteAtraso.TotalSeconds
            ? HealthCheckResult.Degraded("Há triagem pendente há mais de 5 minutos.", data: dados)
            : HealthCheckResult.Healthy(data: dados);
    }

    private sealed record EstadoFila(int Pendentes, int? MaisAntigaSegundos);
}
