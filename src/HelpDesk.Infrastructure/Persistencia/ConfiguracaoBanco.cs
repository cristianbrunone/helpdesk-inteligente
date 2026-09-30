using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistencia;

public static class ConfiguracaoBanco
{
    /// <summary>
    /// Configuração única do provedor, usada pelos hosts, pela design-time factory e pelos testes.
    /// O <c>MapEnum</c> registra os enums nativos no Npgsql e faz a migration criar os tipos (modelo §3).
    /// </summary>
    public static DbContextOptionsBuilder Configurar(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql
            .MapEnum<StatusChamado>("status_chamado")
            .MapEnum<Prioridade>("prioridade_chamado")
            .MapEnum<StatusTriagem>("status_triagem"));
}
