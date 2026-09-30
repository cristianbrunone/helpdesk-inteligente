using HelpDesk.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Logs em JSON no stdout: configurados em appsettings.json (ADR-0016).
var intervalo = builder.Configuration.GetValue<int?>(Heartbeat.VariavelIntervalo) is { } segundos and > 0
    ? TimeSpan.FromSeconds(segundos)
    : Heartbeat.IntervaloPadrao;

builder.Services.AddHostedService(sp => new Heartbeat(sp.GetRequiredService<ILogger<Heartbeat>>(), intervalo));

var host = builder.Build();

host.Run();
