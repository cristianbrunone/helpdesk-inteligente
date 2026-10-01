using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistencia;

internal sealed class RepositorioChamados(HelpDeskDbContext db) : IRepositorioChamados
{
    public Task<bool> CategoriaExisteAsync(short categoriaId, CancellationToken cancellationToken) =>
        db.Categorias.AnyAsync(c => c.Id == categoriaId, cancellationToken);

    public void Adicionar(Chamado chamado) => db.Chamados.Add(chamado);

    // Um SaveChanges = uma transação: chamado, histórico e comentários entram juntos ou nenhum entra (RN-02).
    public Task SalvarAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
