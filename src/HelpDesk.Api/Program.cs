using System.Text.Json.Serialization;
using HelpDesk.Api;
using HelpDesk.Api.Endpoints;
using HelpDesk.Api.Erros;
using HelpDesk.Api.Observabilidade;
using HelpDesk.Infrastructure;
using HelpDesk.Infrastructure.Configuracao;
using HelpDesk.Infrastructure.Observabilidade;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Logs em JSON no stdout: configurados em appsettings.json (ADR-0016).
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("A variável ConnectionStrings__Default não foi configurada.");

builder.Services.AdicionarInfraestrutura(connectionString);
// Kill switches de IA (ADR-0021): lidos uma vez na subida; valor inválido impede a API de subir.
var leitor = new LeitorAmbiente(chave => builder.Configuration[chave]);
builder.Services.AddSingleton(leitor.OpcoesIA());
// Tracing (ADR-0019): só com OTEL_EXPORTER_OTLP_ENDPOINT. O /health fica de fora (o Docker o chama a cada 10 s).
builder.Services.AdicionarTracing("helpdesk-api", leitor.EndpointOtlp(), tracing => tracing
    .AddAspNetCoreInstrumentation(opcoes => opcoes.Filter = http => http.Request.Path != SaudeEndpoints.Rota));
builder.Services.AdicionarCasosDeUso();
// Enums só como texto em PascalCase ASCII (contrato §1): "EmAndamento", "Critica". Número é tipo errado (400).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AdicionarProblemDetails();
builder.Services.AdicionarSaude();
builder.Services.AddOpenApi();

var app = builder.Build();

// A correlação vem primeiro, para que até os erros tratados abaixo saiam com CorrelationId.
app.UseMiddleware<CorrelacaoMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Documento OpenAPI nativo + Swagger UI (só a UI) apontando para ele (ADR-0013).
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "HelpDesk API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "HelpDesk Inteligente — API";
});

app.MapSaude();
app.MapCategorias();
app.MapChamados();
app.MapTriagem();
app.MapConfiguracao();
app.MapDashboard();

app.Run();

// Exposto para o WebApplicationFactory dos testes de integração.
public partial class Program;
