using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Domain.Usuarios;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistencia;

public static class ConfiguracaoBanco
{
    /// <summary>
    /// Configuração única do provedor, usada pelos hosts, pela design-time factory e pelos testes.
    /// O <c>MapEnum</c> registra os enums nativos no Npgsql e faz a migration criar os tipos (modelo §3);
    /// o <c>UseVector</c> mapeia a coluna <c>vector</c> do pgvector (ADR-0007).
    /// </summary>
    public static DbContextOptionsBuilder Configurar(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql
            .UseVector()
            .MapEnum<StatusChamado>("status_chamado")
            .MapEnum<Prioridade>("prioridade_chamado")
            .MapEnum<StatusTriagem>("status_triagem")
            .MapEnum<PerfilUsuario>("perfil_usuario"));
}
