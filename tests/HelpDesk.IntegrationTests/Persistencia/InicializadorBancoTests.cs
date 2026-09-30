using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Persistencia;

public sealed class InicializadorBancoTests(BancoFixture banco)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrarEAplicarSeed_ExecutadoDuasVezes_MantemAsCincoCategoriasSemDuplicar()
    {
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

    private async Task ExecutarInicializadorAsync()
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        await escopo.ServiceProvider.GetRequiredService<InicializadorBanco>().MigrarEAplicarSeedAsync(Ct);
    }

    private async Task<T> ConsultarAsync<T>(Func<HelpDeskDbContext, Task<T>> consulta)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }
}
