namespace HelpDesk.Application.Categorias;

/// <summary>Categorias usadas nos filtros e no formulário (contrato: <c>GET /api/categorias</c>).</summary>
public sealed class ListarCategorias(IConsultaCategorias consulta)
{
    public Task<IReadOnlyList<CategoriaResumo>> ExecutarAsync(CancellationToken cancellationToken) =>
        consulta.ListarAsync(cancellationToken);
}
