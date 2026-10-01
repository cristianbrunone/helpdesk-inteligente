using HelpDesk.Domain.Categorias;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>Aplica as migrations e o seed idempotente. Executado só pelo migrator e pelos testes (ADR-0015).</summary>
public sealed class InicializadorBanco(HelpDeskDbContext db, ILogger<InicializadorBanco> logger)
{
    private static readonly string[] _categoriasPadrao =
        ["Acesso/Login", "Financeiro", "Bug no sistema", "Dúvida", "Infraestrutura"];

    public async Task MigrarEAplicarSeedAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await AplicarSeedCategoriasAsync(cancellationToken);
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
}
