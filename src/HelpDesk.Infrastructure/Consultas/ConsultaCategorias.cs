using HelpDesk.Application.Categorias;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Consultas;

internal sealed class ConsultaCategorias(HelpDeskDbContext db) : IConsultaCategorias
{
    public async Task<IReadOnlyList<CategoriaResumo>> ListarAsync(CancellationToken cancellationToken) =>
        await db.Categorias
            .AsNoTracking()
            .OrderBy(c => c.Nome)
            .Select(c => new CategoriaResumo(c.Id, c.Nome))
            .ToListAsync(cancellationToken);
}
