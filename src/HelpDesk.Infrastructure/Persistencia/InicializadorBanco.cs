using HelpDesk.Domain.Categorias;
using HelpDesk.Infrastructure.Persistencia.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>Aplica as migrations e o seed idempotente. Executado só pelo migrator e pelos testes (ADR-0015).</summary>
public sealed class InicializadorBanco(
    HelpDeskDbContext db,
    TimeProvider relogio,
    ILogger<InicializadorBanco> logger)
{
    private static readonly string[] _categoriasPadrao =
        ["Acesso/Login", "Financeiro", "Bug no sistema", "Dúvida", "Infraestrutura"];

    public async Task MigrarEAplicarSeedAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await AplicarSeedCategoriasAsync(cancellationToken);
        await AplicarSeedChamadosAsync(cancellationToken);
    }

    private async Task AplicarSeedCategoriasAsync(CancellationToken cancellationToken)
    {
        var existentes = await db.Categorias.Select(c => c.Nome).ToListAsync(cancellationToken);
        var novas = _categoriasPadrao.Except(existentes).Select(nome => new Categoria(nome)).ToList();

        db.Categorias.AddRange(novas);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seed de categorias: {Inseridas} inseridas, {Existentes} já existiam",
            novas.Count, existentes.Count);
    }

    // Só roda com a tabela vazia (modelo §7): nunca mistura dados de demonstração com dados reais.
    private async Task AplicarSeedChamadosAsync(CancellationToken cancellationToken)
    {
        if (await db.Chamados.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Seed de chamados ignorado: a tabela já tem dados");
            return;
        }

        var categorias = await db.Categorias.ToDictionaryAsync(c => c.Nome, c => c.Id, cancellationToken);
        var chamados = GeradorSeedChamados.Gerar(categorias, relogio.GetUtcNow());

        db.Chamados.AddRange(chamados);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seed de chamados: {Chamados} chamados, {Comentarios} comentários e {Historico} registros de histórico",
            chamados.Count, chamados.Sum(c => c.Comentarios.Count), chamados.Sum(c => c.Historico.Count));
    }
}
