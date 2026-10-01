using System.Globalization;
using HelpDesk.Application;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistencia;

internal sealed class RepositorioChamados(HelpDeskDbContext db) : IRepositorioChamados
{
    public Task<bool> CategoriaExisteAsync(short categoriaId, CancellationToken cancellationToken) =>
        db.Categorias.AnyAsync(c => c.Id == categoriaId, cancellationToken);

    public void Adicionar(Chamado chamado) => db.Chamados.Add(chamado);

    public Task<Chamado?> ObterParaAlteracaoAsync(Guid id, CancellationToken cancellationToken) =>
        db.Chamados
            .AsSingleQuery()
            .Include(c => c.Comentarios)
            .Include(c => c.Historico)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    // O valor original do xmin é o lido do banco: é ele que o EF compara no UPDATE ... WHERE xmin = @original.
    public string Versao(Chamado chamado) =>
        db.Entry(chamado).Property<uint>(HelpDeskDbContext.VersaoChamado).OriginalValue
            .ToString(CultureInfo.InvariantCulture);

    // Um SaveChanges = uma transação: chamado, histórico e comentários entram juntos ou nenhum entra (RN-02).
    public async Task SalvarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra gravação mudou o xmin entre a nossa leitura e o UPDATE: mesmo caso do If-Match velho (412).
            throw new VersaoDesatualizadaException();
        }
    }
}
