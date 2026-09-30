using HelpDesk.Infrastructure;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(HelpDesk.IntegrationTests.Infraestrutura.BancoFixture))]

namespace HelpDesk.IntegrationTests.Infraestrutura;

/// <summary>
/// Um PostgreSQL real (com pgvector) por execução da suíte, na mesma imagem do docker-compose. Sobe já com as
/// migrations e o seed aplicados pelo mesmo código do migrator (ADR-0015).
/// </summary>
public sealed class BancoFixture : IAsyncLifetime
{
    public const string Imagem = "pgvector/pgvector:0.8.6-pg18";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Imagem).Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var servicos = CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        await escopo.ServiceProvider.GetRequiredService<InicializadorBanco>().MigrarEAplicarSeedAsync(default);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Mesma composição da Infrastructure usada pelos hosts.</summary>
    public ServiceProvider CriarServicos()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AdicionarInfraestrutura(ConnectionString);
        return services.BuildServiceProvider();
    }
}
