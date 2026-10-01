using HelpDesk.Application.Chamados;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HelpDesk.Api.Endpoints;

internal static class ChamadosEndpoints
{
    public static IEndpointRouteBuilder MapChamados(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/chamados").WithTags("Chamados");

        grupo.MapPost("/", Criar)
            .WithName("CriarChamado")
            .WithSummary("Abre um chamado. Responde na hora, sem aguardar a IA.")
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<Created<ChamadoDetalhe>> Criar(
        NovoChamado corpo, CriarChamado casoDeUso, HttpContext http, CancellationToken cancellationToken)
    {
        var criado = await casoDeUso.ExecutarAsync(corpo, cancellationToken);
        http.Response.Headers.ETag = ETag.De(criado.Versao);
        return TypedResults.Created($"/api/chamados/{criado.Chamado.Id}", criado.Chamado);
    }
}
