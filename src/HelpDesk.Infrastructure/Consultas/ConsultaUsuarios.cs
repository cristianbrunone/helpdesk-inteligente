using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Usuarios;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Consultas;

/// <summary>Leitura dos usuários (ADR-0026): o e-mail chega normalizado, e o índice único <c>ix_usuarios_email</c> atende.</summary>
internal sealed class ConsultaUsuarios(HelpDeskDbContext db) : IConsultaUsuarios
{
    public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Usuarios.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
}
