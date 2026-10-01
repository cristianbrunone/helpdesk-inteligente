using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Persistencia;

public sealed class InicializadorBancoTests(BancoFixture banco)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrarEAplicarSeed_ExecutadoNovamente_MantemAsCincoCategoriasSemDuplicar()
    {
        // A fixture já executou o inicializador uma vez ao subir o banco.
        await ExecutarInicializadorAsync();
        await ExecutarInicializadorAsync();

        var nomes = await ConsultarAsync(db => db.Categorias.OrderBy(c => c.Id).Select(c => c.Nome).ToListAsync(Ct));

        nomes.ShouldBe(["Acesso/Login", "Financeiro", "Bug no sistema", "Dúvida", "Infraestrutura"]);
    }

    [Fact]
    public async Task MigrarEAplicarSeed_Executado_CriaAsExtensoesNecessarias()
    {
        await ExecutarInicializadorAsync();

        var extensoes = await ConsultarAsync(db => db.Database
            .SqlQuery<string>($"SELECT extname AS \"Value\" FROM pg_extension")
            .ToListAsync(Ct));

        extensoes.ShouldContain("vector");
        extensoes.ShouldContain("pg_trgm");
        extensoes.ShouldContain("unaccent");
    }

    [Theory]
    [InlineData("prioridade_chamado", new[] { "baixa", "media", "alta", "critica" })]
    [InlineData("status_chamado", new[] { "aberto", "em_andamento", "resolvido", "fechado", "cancelado" })]
    [InlineData("status_triagem", new[] { "pendente", "concluida", "falhou", "aceita", "rejeitada" })]
    public async Task MigrarEAplicarSeed_Executado_CriaEnumComRotulosNaOrdemDeNegocio(string tipo, string[] esperados)
    {
        await ExecutarInicializadorAsync();

        var rotulos = await ConsultarAsync(db => db.Database
            .SqlQuery<string>($"""
                SELECT e.enumlabel::text AS "Value"
                FROM pg_enum e JOIN pg_type t ON t.oid = e.enumtypid
                WHERE t.typname = {tipo}
                ORDER BY e.enumsortorder
                """)
            .ToListAsync(Ct));

        rotulos.ShouldBe(esperados);
    }

    [Fact]
    public async Task MigrarEAplicarSeed_BancoVazioExecutadoDuasVezes_InsereOs200ChamadosUmaUnicaVez()
    {
        var bancoIsolado = await banco.CriarBancoVazioAsync(Ct);

        await ExecutarInicializadorAsync(bancoIsolado);
        await ExecutarInicializadorAsync(bancoIsolado);

        var totais = await ConsultarAsync(db => db.Database
            .SqlQuery<int>($"""
                SELECT (SELECT count(*) FROM chamados)::int AS "Value"
                UNION ALL
                -- chamados cujo último registro de histórico não bate com o status atual (deve ser zero)
                SELECT count(*)::int FROM chamados c
                WHERE c.status <> (SELECT h.status_novo FROM historico_status h
                                   WHERE h.chamado_id = c.id ORDER BY h.alterado_em DESC LIMIT 1)
                UNION ALL
                SELECT count(*)::int FROM chamados c
                WHERE NOT EXISTS (SELECT 1 FROM comentarios m WHERE m.chamado_id = c.id)
                """)
            .ToListAsync(Ct), bancoIsolado);

        totais.ShouldBe([200, 0, 0]);
    }

    private async Task ExecutarInicializadorAsync(string? connectionString = null)
    {
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        await escopo.ServiceProvider.GetRequiredService<InicializadorBanco>().MigrarEAplicarSeedAsync(Ct);
    }

    private async Task<T> ConsultarAsync<T>(Func<HelpDeskDbContext, Task<T>> consulta, string? connectionString = null)
    {
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }
}
