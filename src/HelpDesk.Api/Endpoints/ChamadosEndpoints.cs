using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HelpDesk.Api.Endpoints;

internal static class ChamadosEndpoints
{
    public static IEndpointRouteBuilder MapChamados(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/chamados").WithTags("Chamados");

        grupo.MapGet("/", Listar)
            .WithName("ListarChamados")
            .WithSummary("Lista chamados com filtros (repetíveis = OR), busca sem acento, ordenação e paginação.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        grupo.MapGet("/{id:guid}", Obter)
            .WithName("ObterChamado")
            .WithSummary("Detalhe com comentários, histórico e as transições permitidas. O ETag carrega a versão.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPost("/", Criar)
            .WithName("CriarChamado")
            .WithSummary("Abre um chamado. Responde na hora, sem aguardar a IA.")
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<Ok<ResultadoPaginado<ChamadoResumo>>> Listar(
        [AsParameters] ParametrosListagemHttp parametros, ListarChamados casoDeUso, CancellationToken cancellationToken) =>
        TypedResults.Ok(await casoDeUso.ExecutarAsync(parametros.ParaAplicacao(), cancellationToken));

    private static async Task<Ok<ChamadoDetalhe>> Obter(
        Guid id, ObterChamado casoDeUso, HttpContext http, CancellationToken cancellationToken)
    {
        var encontrado = await casoDeUso.ExecutarAsync(id, cancellationToken);
        http.Response.Headers.ETag = ETag.De(encontrado.Versao);
        return TypedResults.Ok(encontrado.Chamado);
    }

    private static async Task<Created<ChamadoDetalhe>> Criar(
        NovoChamado corpo, CriarChamado casoDeUso, HttpContext http, CancellationToken cancellationToken)
    {
        var criado = await casoDeUso.ExecutarAsync(corpo, cancellationToken);
        http.Response.Headers.ETag = ETag.De(criado.Versao);
        return TypedResults.Created($"/api/chamados/{criado.Chamado.Id}", criado.Chamado);
    }
}

/// <summary>Query string de <c>GET /api/chamados</c> com os nomes do contrato (camelCase no OpenAPI).</summary>
internal sealed record ParametrosListagemHttp(
    [property: FromQuery(Name = "status")] StatusChamado[]? Status,
    [property: FromQuery(Name = "prioridade")] Prioridade[]? Prioridade,
    [property: FromQuery(Name = "categoriaId")] short[]? CategoriaId,
    [property: FromQuery(Name = "semCategoria")] bool? SemCategoria,
    [property: FromQuery(Name = "q")] string? Q,
    [property: FromQuery(Name = "criadoDe")] DateOnly? CriadoDe,
    [property: FromQuery(Name = "criadoAte")] DateOnly? CriadoAte,
    [property: FromQuery(Name = "ordenarPor")] string? OrdenarPor,
    [property: FromQuery(Name = "direcao")] string? Direcao,
    [property: FromQuery(Name = "pagina")] int? Pagina,
    [property: FromQuery(Name = "tamanhoPagina")] int? TamanhoPagina)
{
    public ParametrosListagem ParaAplicacao() => new(
        Status ?? [], Prioridade ?? [], CategoriaId ?? [], SemCategoria ?? false,
        Q, CriadoDe, CriadoAte, OrdenarPor, Direcao, Pagina, TamanhoPagina);
}
