using HelpDesk.Infrastructure;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
    public ServiceProvider CriarServicos(string? connectionString = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AdicionarInfraestrutura(connectionString ?? ConnectionString);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Um banco vazio no mesmo container, para testes que precisam de contagens exatas sem a interferência dos
    /// outros testes (que rodam em paralelo no banco compartilhado).
    /// </summary>
    public async Task<string> CriarBancoVazioAsync(CancellationToken cancellationToken)
    {
        var nome = $"isolado_{Guid.NewGuid():N}";
        await using (var conexao = new NpgsqlConnection(ConnectionString))
        {
            await conexao.OpenAsync(cancellationToken);
            await using var comando = new NpgsqlCommand($"CREATE DATABASE {nome}", conexao);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = nome }.ConnectionString;
    }
}
