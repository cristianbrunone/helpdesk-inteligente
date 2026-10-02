using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Usuarios;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.Infrastructure.Persistencia.Seed;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelpDesk.IntegrationTests.Persistencia;

/// <summary>A tabela <c>usuarios</c> e o seed de demonstração (ADR-0026) no PostgreSQL real.</summary>
public sealed class UsuariosPersistenciaTests(BancoFixture banco)
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seed_ExecutadoDuasVezes_QuatroUsuariosSemDuplicarNemTrocarAsSenhas()
    {
        var connectionString = await banco.CriarBancoMigradoAsync(Ct);
        var antes = await HashesAsync(connectionString);

        await using (var servicos = banco.CriarServicos(connectionString))
        await using (var escopo = servicos.CreateAsyncScope())
        {
            await escopo.ServiceProvider.GetRequiredService<InicializadorBanco>().MigrarEAplicarSeedAsync(Ct);
        }

        var depois = await HashesAsync(connectionString);
        depois.Count.ShouldBe(4);
        depois.ShouldBe(antes);
    }

    [Fact]
    public async Task Seed_UsuariosDeDemonstracao_PerfisCertosESenhaDeDemonstracaoConfere()
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var hasher = escopo.ServiceProvider.GetRequiredService<IHashSenha>();

        var usuarios = await db.Usuarios.AsNoTracking().ToListAsync(Ct);

        foreach (var esperado in GeradorSeedUsuarios.Todos)
        {
            var usuario = usuarios.Single(u => u.Email == esperado.Email);
            (usuario.Nome, usuario.Perfil).ShouldBe((esperado.Nome, esperado.Perfil));
            hasher.Verificar(GeradorSeedUsuarios.SenhaDemonstracao, usuario.SenhaHash).ShouldBeTrue();
            usuario.SenhaHash.ShouldNotContain(GeradorSeedUsuarios.SenhaDemonstracao);
        }
    }

    [Fact]
    public async Task Gravar_MesmoEmail_IndiceUnicoRecusa()
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();

        db.Usuarios.Add(Usuario.Criar("Outra Ana", "ANA.SUPORTE@example.com", PerfilUsuario.Solicitante, "hash", _agora));

        var erro = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        erro.InnerException.ShouldBeOfType<PostgresException>().ConstraintName.ShouldBe("ix_usuarios_email");
    }

    [Theory]
    [InlineData("INSERT INTO usuarios (id, nome, email, perfil, senha_hash, criado_em) VALUES (gen_random_uuid(), 'X', 'Maiuscula@example.com', 'atendente', 'h', now())", "ck_usuarios_email_minusculo")]
    [InlineData("INSERT INTO usuarios (id, nome, email, perfil, senha_hash, criado_em) VALUES (gen_random_uuid(), '  ', 'branco@example.com', 'atendente', 'h', now())", "ck_usuarios_nome_preenchido")]
    [InlineData("INSERT INTO usuarios (id, nome, email, perfil, senha_hash, criado_em) VALUES (gen_random_uuid(), 'X', 'sem-arroba', 'atendente', 'h', now())", "ck_usuarios_email_formato")]
    public async Task Gravar_ForaDaApi_ChecksDoBancoRecusam(string sql, string constraint)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();

        var erro = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Ct));

        erro.ConstraintName.ShouldBe(constraint);
    }

    private async Task<Dictionary<string, string>> HashesAsync(string connectionString)
    {
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>().Usuarios
            .ToDictionaryAsync(u => u.Email, u => u.SenhaHash, Ct);
    }
}
