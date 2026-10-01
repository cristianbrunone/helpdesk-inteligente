using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>Mesmo <see cref="HelpDeskDbContext"/> (escopo da requisição) do repositório de chamados: uma transação.</summary>
internal sealed class RepositorioTriagens(HelpDeskDbContext db) : IRepositorioTriagens
{
    public void Adicionar(TriagemIA triagem) => db.Triagens.Add(triagem);
}
