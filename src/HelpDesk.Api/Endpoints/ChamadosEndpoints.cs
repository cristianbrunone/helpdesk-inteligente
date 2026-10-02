using System.Security.Claims;
using HelpDesk.Api.Autenticacao;
using HelpDesk.Application.Autenticacao;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Usuarios;
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

        grupo.MapPatch("/{id:guid}/status", MudarStatus)
            .RequireAuthorization(ConfiguracaoAutenticacao.PoliticaAtendente)
            .WithName("MudarStatusChamado")
            .WithSummary("Muda o status pela máquina de estados. If-Match opcional (412 se desatualizado).")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/{id:guid}/comentarios", Comentar)
            .WithName("AdicionarComentario")
            .WithSummary("Comenta o chamado. Fechado e Cancelado recusam (409). O ETag devolvido é a nova versão do chamado.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<Ok<ResultadoPaginado<ChamadoResumo>>> Listar(
        [AsParameters] ParametrosListagemHttp parametros, ClaimsPrincipal principal, ListarChamados casoDeUso, CancellationToken cancellationToken) =>
        TypedResults.Ok(await casoDeUso.ExecutarAsync(parametros.ParaAplicacao(), principal.ObterUsuario(), cancellationToken));

    private static async Task<Ok<ChamadoDetalhe>> Obter(
        Guid id, ClaimsPrincipal principal, ObterChamado casoDeUso, HttpContext http, CancellationToken cancellationToken)
    {
        var encontrado = await casoDeUso.ExecutarAsync(id, principal.ObterUsuario(), cancellationToken);
        http.Response.Headers.ETag = ETag.De(encontrado.Versao);
        return TypedResults.Ok(encontrado.Chamado);
    }

    private static async Task<Ok<ChamadoDetalhe>> MudarStatus(
        Guid id, MudancaDeStatus corpo, MudarStatusChamado casoDeUso, HttpContext http, CancellationToken cancellationToken)
    {
        var alterado = await casoDeUso.ExecutarAsync(id, corpo, ETag.VersoesDoIfMatch(http.Request), cancellationToken);
        http.Response.Headers.ETag = ETag.De(alterado.Versao);
        return TypedResults.Ok(alterado.Chamado);
    }

    private static async Task<Created<ComentarioDetalhe>> Comentar(
        Guid id, NovoComentario corpo, ClaimsPrincipal principal, AdicionarComentario casoDeUso, HttpContext http, CancellationToken cancellationToken)
    {
        var criado = await casoDeUso.ExecutarAsync(id, corpo, principal.ObterUsuario(), ETag.VersoesDoIfMatch(http.Request), cancellationToken);
        http.Response.Headers.ETag = ETag.De(criado.VersaoChamado);
        // O comentário não tem rota própria: o Location aponta para o chamado, onde ele aparece.
        return TypedResults.Created($"/api/chamados/{id}", criado.Comentario);
    }

    private static async Task<Created<ChamadoDetalhe>> Criar(
        NovoChamado corpo, ClaimsPrincipal principal, CriarChamado casoDeUso, HttpContext http, CancellationToken cancellationToken)
    {
        var usuario = principal.ObterUsuario();
        var dados = usuario.Perfil == PerfilUsuario.Solicitante
            ? corpo with { SolicitanteNome = usuario.Nome, SolicitanteEmail = usuario.Email }
            : corpo;
        var criado = await casoDeUso.ExecutarAsync(dados, cancellationToken);
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
