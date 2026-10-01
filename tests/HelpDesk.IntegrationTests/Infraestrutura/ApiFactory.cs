using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.IntegrationTests.Infraestrutura;

/// <summary>A API real em memória, ligada ao PostgreSQL do Testcontainers, com os logs capturados.</summary>
public sealed class ApiFactory(BancoFixture banco) : WebApplicationFactory<Program>
{
    public LogsCapturados Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", banco.ConnectionString);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    public IServiceScope CriarEscopo() => Services.CreateScope();
}

/// <summary>A API apontando para uma porta sem banco, para simular o banco fora do ar.</summary>
public sealed class ApiSemBancoFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default",
            "Host=127.0.0.1;Port=1;Database=inexistente;Username=x;Password=x;Timeout=1");
}
