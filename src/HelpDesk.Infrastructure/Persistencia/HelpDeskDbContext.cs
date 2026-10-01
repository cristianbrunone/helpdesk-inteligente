using HelpDesk.Domain.Categorias;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Conhecimento;
using HelpDesk.Domain.Triagem;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Persistencia;

public sealed class HelpDeskDbContext(DbContextOptions<HelpDeskDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Propriedade-sombra do <see cref="Chamado"/> mapeada no <c>xmin</c> do PostgreSQL: é o token de concorrência
    /// otimista do EF e o valor do <c>ETag</c> da API.
    /// </summary>
    public const string VersaoChamado = "Versao";

    public DbSet<Categoria> Categorias => Set<Categoria>();

    public DbSet<Chamado> Chamados => Set<Chamado>();

    public DbSet<TriagemIA> Triagens => Set<TriagemIA>();

    public DbSet<RegistroUsoLlm> UsoLlm => Set<RegistroUsoLlm>();

    public DbSet<ArtigoConhecimento> Artigos => Set<ArtigoConhecimento>();

    public DbSet<DocumentoRag> DocumentosRag => Set<DocumentoRag>();

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

        modelBuilder.HasDbFunction(typeof(HelpDeskDbContext).GetMethod(nameof(FUnaccent))!).HasName("f_unaccent");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HelpDeskDbContext).Assembly);
        modelBuilder.AplicarSnakeCase();
    }

    /// <summary>
    /// Função <c>f_unaccent</c> criada na migration (ADR-0008). Usada nas consultas com a mesma expressão do índice
    /// <c>ix_chamados_busca_trgm</c>, para o planner poder usá-lo.
    /// </summary>
    public static string FUnaccent(string texto) =>
        throw new NotSupportedException("FUnaccent só pode ser usada dentro de consultas do EF Core.");

    private static string[] Rotulos<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(valor => NomesSnakeCase.Converter(valor.ToString()))];
}
