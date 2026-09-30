using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.IntegrationTests.Infraestrutura;

/// <summary>A API real em memória, com os logs capturados para asserção.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public LogsCapturados Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

    public IServiceScope CriarEscopo() => Services.CreateScope();
}
