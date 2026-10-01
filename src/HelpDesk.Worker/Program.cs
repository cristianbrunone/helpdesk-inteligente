using HelpDesk.Infrastructure.Configuracao;
using HelpDesk.Infrastructure.Observabilidade;
using HelpDesk.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Logs em JSON no stdout: configurados em appsettings.json (ADR-0016).
var intervalo = builder.Configuration.GetValue<int?>(Heartbeat.VariavelIntervalo) is { } segundos and > 0
    ? TimeSpan.FromSeconds(segundos)
    : Heartbeat.IntervaloPadrao;
builder.Services.AddHostedService(sp => new Heartbeat(sp.GetRequiredService<ILogger<Heartbeat>>(), intervalo));

// Configuração lida e validada na subida: valor inválido impede o Worker de subir, com mensagem clara.
var leitor = new LeitorAmbiente(chave => builder.Configuration[chave]);
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("A variável ConnectionStrings__Default não foi configurada.");
var opcoesLlm = leitor.OpcoesLlm();
builder.Services.AdicionarTriagem(
    connectionString, leitor.OpcoesIA(), opcoesLlm, OpcoesFila.Ler(leitor, opcoesLlm), leitor.OpcoesRag(),
    leitor.VersaoPromptTriagem());
builder.Services.AdicionarIndexacao(OpcoesReconciliacao.Ler(leitor));
// Tracing (ADR-0019): só com OTEL_EXPORTER_OTLP_ENDPOINT.
builder.Services.AdicionarTracing("helpdesk-worker", leitor.EndpointOtlp());

var host = builder.Build();

// O prompt configurado precisa existir e ter os marcadores: melhor não subir do que falhar cada triagem.
await host.Services.GetRequiredService<HelpDesk.Application.Triagem.MontadorPromptTriagem>().ValidarAsync(default);

await host.RunAsync();
