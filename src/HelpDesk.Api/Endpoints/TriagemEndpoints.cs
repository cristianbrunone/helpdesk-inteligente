using HelpDesk.Application.Chamados;
using HelpDesk.Application.Triagem;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HelpDesk.Api.Endpoints;

/// <summary>Triagem por IA de um chamado (contrato §3): refazer, aceitar e rejeitar a triagem vigente.</summary>
internal static class TriagemEndpoints
{
    public static IEndpointRouteBuilder MapTriagem(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/chamados/{id:guid}/triagem").WithTags("Triagem por IA");

        grupo.MapPost("/", Refazer)
            .WithName("RefazerTriagem")
            .WithSummary("Cria uma nova triagem pendente (as anteriores ficam no histórico). Não espera a IA.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        grupo.MapPost("/aceitar", Aceitar)
            .WithName("AceitarTriagem")
            .WithSummary("Aplica a categoria e a prioridade sugeridas ao chamado. If-Match opcional.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/rejeitar", Rejeitar)
            .WithName("RejeitarTriagem")
            .WithSummary("Registra a rejeição (com motivo opcional) sem alterar o chamado. If-Match opcional.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<Accepted<TriagemDetalhe>> Refazer(
        Guid id, RefazerTriagem casoDeUso, CancellationToken cancellationToken) =>
        TypedResults.Accepted($"/api/chamados/{id}", await casoDeUso.ExecutarAsync(id, cancellationToken));

    private static async Task<Ok<ChamadoDetalhe>> Aceitar(
        Guid id, AceiteTriagem corpo, DecidirTriagem casoDeUso, HttpContext http, CancellationToken cancellationToken)
    {
        var decidido = await casoDeUso.AceitarAsync(id, corpo, ETag.VersoesDoIfMatch(http.Request), cancellationToken);
        http.Response.Headers.ETag = ETag.De(decidido.Versao);
        return TypedResults.Ok(decidido.Chamado);
    }

    private static async Task<Ok<ChamadoDetalhe>> Rejeitar(
        Guid id, RejeicaoTriagem corpo, DecidirTriagem casoDeUso, HttpContext http, CancellationToken cancellationToken)
    {
        var decidido = await casoDeUso.RejeitarAsync(id, corpo, ETag.VersoesDoIfMatch(http.Request), cancellationToken);
        http.Response.Headers.ETag = ETag.De(decidido.Versao);
        return TypedResults.Ok(decidido.Chamado);
    }
}
