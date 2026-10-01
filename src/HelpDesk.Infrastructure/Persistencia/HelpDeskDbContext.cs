using HelpDesk.Domain.Categorias;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistencia;

public sealed class HelpDeskDbContext(DbContextOptions<HelpDeskDbContext> options) : DbContext(options)
{
    public DbSet<Categoria> Categorias => Set<Categoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Busca vetorial (ADR-0007); busca por substring e sem acento (ADR-0008).
        modelBuilder
            .HasPostgresExtension("vector")
            .HasPostgresExtension("pg_trgm")
            .HasPostgresExtension("unaccent");

        // Rótulos explícitos: sozinho, o MapEnum gera o CREATE TYPE em ordem alfabética, e a ordem de
        // declaração é a ordem de negócio (P-07: ORDER BY prioridade = Baixa < Média < Alta < Crítica).
        modelBuilder
            .HasPostgresEnum("status_chamado", Rotulos<StatusChamado>())
            .HasPostgresEnum("prioridade_chamado", Rotulos<Prioridade>())
            .HasPostgresEnum("status_triagem", Rotulos<StatusTriagem>());

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HelpDeskDbContext).Assembly);
        modelBuilder.AplicarSnakeCase();
    }

    private static string[] Rotulos<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(valor => NomesSnakeCase.Converter(valor.ToString()))];
}
