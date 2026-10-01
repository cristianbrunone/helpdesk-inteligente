using HelpDesk.Application.Categorias;

namespace HelpDesk.Api.Endpoints;

internal static class CategoriasEndpoints
{
    public static IEndpointRouteBuilder MapCategorias(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/categorias").WithTags("Categorias");

        grupo.MapGet("/", async (ListarCategorias casoDeUso, CancellationToken cancellationToken) =>
                TypedResults.Ok(await casoDeUso.ExecutarAsync(cancellationToken)))
            .WithName("ListarCategorias")
            .WithSummary("Lista as categorias de chamado, ordenadas por nome.");

        return app;
    }
}
