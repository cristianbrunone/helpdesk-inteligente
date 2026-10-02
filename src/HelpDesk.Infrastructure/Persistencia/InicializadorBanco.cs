using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Categorias;
using HelpDesk.Infrastructure.Persistencia.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>Aplica as migrations e o seed idempotente. Executado só pelo migrator e pelos testes (ADR-0015).</summary>
public sealed class InicializadorBanco(
    HelpDeskDbContext db,
    TimeProvider relogio,
    IHashSenha hasher,
    ILogger<InicializadorBanco> logger)
{
    private static readonly string[] _categoriasPadrao =
        ["Acesso/Login", "Financeiro", "Bug no sistema", "Dúvida", "Infraestrutura"];

    public async Task MigrarEAplicarSeedAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await AplicarSeedCategoriasAsync(cancellationToken);
        await AplicarSeedChamadosAsync(cancellationToken);
        await AplicarSeedArtigosAsync(cancellationToken);
        await AplicarSeedUsuariosAsync(cancellationToken);
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
        var (chamados, triagens) = GeradorSeedChamados.Gerar(categorias, relogio.GetUtcNow());

        db.Chamados.AddRange(chamados);
        db.Triagens.AddRange(triagens);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seed de chamados: {Chamados} chamados, {Comentarios} comentários, {Historico} registros de histórico e {Triagens} triagens",
            chamados.Count, chamados.Sum(c => c.Comentarios.Count), chamados.Sum(c => c.Historico.Count), triagens.Count);
    }

    // Idempotente como o de chamados, mas independente dele: um banco que já tinha chamados antes da Sprint 3
    // também recebe a base de conhecimento.
    private async Task AplicarSeedArtigosAsync(CancellationToken cancellationToken)
    {
        if (await db.Artigos.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Seed de artigos ignorado: a tabela já tem dados");
            return;
        }

        var categorias = await db.Categorias.ToDictionaryAsync(c => c.Nome, c => c.Id, cancellationToken);
        var artigos = GeradorSeedArtigos.Gerar(categorias, relogio.GetUtcNow());

        db.Artigos.AddRange(artigos);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seed de artigos: {Artigos} artigos", artigos.Count);
    }

    // Por e-mail, como o de categorias (ADR-0026): um banco anterior à Sprint 6 também recebe os usuários de
    // demonstração, e um usuário que já existe nunca é sobrescrito (a senha dele continua a mesma).
    private async Task AplicarSeedUsuariosAsync(CancellationToken cancellationToken)
    {
        var existentes = await db.Usuarios.Select(u => u.Email).ToListAsync(cancellationToken);
        var novos = GeradorSeedUsuarios.Todos.Any(u => !existentes.Contains(u.Email))
            ? GeradorSeedUsuarios.Gerar(hasher, relogio.GetUtcNow()).Where(u => !existentes.Contains(u.Email)).ToList()
            : [];

        db.Usuarios.AddRange(novos);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seed de usuários: {Inseridos} inseridos, {Existentes} já existiam",
            novos.Count, existentes.Count);
    }
}
