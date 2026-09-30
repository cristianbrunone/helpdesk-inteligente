using System.Runtime.InteropServices;
using HelpDesk.Infrastructure;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Serviço one-shot (ADR-0015): aplica as migrations e o seed e termina com 0 (sucesso) ou 1 (falha).
var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("A variável ConnectionStrings__Default não foi configurada.");
builder.Services.AdicionarInfraestrutura(connectionString);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

// Ctrl+C e docker stop (SIGTERM) cancelam a operação em andamento.
using var cancelamento = new CancellationTokenSource();
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Cancelar);
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Cancelar);

try
{
    await using var escopo = host.Services.CreateAsyncScope();
    var inicializador = escopo.ServiceProvider.GetRequiredService<InicializadorBanco>();
    await inicializador.MigrarEAplicarSeedAsync(cancelamento.Token);

    logger.LogInformation("Migrations e seed aplicados com sucesso");
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "Falha ao aplicar migrations e seed");
    return 1;
}

void Cancelar(PosixSignalContext contexto)
{
    contexto.Cancel = true;
    cancelamento.Cancel();
}
