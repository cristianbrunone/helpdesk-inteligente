using HelpDesk.Api.Autenticacao;
using HelpDesk.Application.Dashboard;

namespace HelpDesk.Api.Endpoints;

internal static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/dashboard").WithTags("Dashboard")
            .RequireAuthorization(ConfiguracaoAutenticacao.PoliticaAtendente);

        grupo.MapGet("/resumo", async (ObterResumoDashboard casoDeUso, CancellationToken cancellationToken) =>
                TypedResults.Ok(await casoDeUso.ExecutarAsync(cancellationToken)))
            .WithName("ObterResumoDashboard")
            .WithSummary("Totais por status e prioridade, tempo médio de resolução por categoria e métricas da IA.");

        return app;
    }
}
