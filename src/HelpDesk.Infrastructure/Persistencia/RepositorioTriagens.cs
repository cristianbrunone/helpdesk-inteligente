using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Triagem;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>Mesmo <see cref="HelpDeskDbContext"/> (escopo da requisição) do repositório de chamados: uma transação.</summary>
internal sealed class RepositorioTriagens(HelpDeskDbContext db) : IRepositorioTriagens
{
    public void Adicionar(TriagemIA triagem) => db.Triagens.Add(triagem);

    public Task<TriagemIA?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        db.Triagens.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<TriagemIA?> ObterVigenteAsync(Guid chamadoId, CancellationToken cancellationToken) =>
        db.Triagens
            .Where(t => t.ChamadoId == chamadoId)
            .OrderByDescending(t => t.CriadoEm)
            .ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
