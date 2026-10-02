using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using HelpDesk.Api;
using HelpDesk.Api.Autenticacao;
using HelpDesk.Api.Endpoints;
using HelpDesk.Api.Erros;
using HelpDesk.Api.Observabilidade;
using HelpDesk.Infrastructure;
using HelpDesk.Infrastructure.Configuracao;
using HelpDesk.Infrastructure.Observabilidade;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Logs em JSON no stdout: configurados em appsettings.json (ADR-0016).
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("A variável ConnectionStrings__Default não foi configurada.");

builder.Services.AdicionarInfraestrutura(connectionString);
// Kill switches de IA (ADR-0021): lidos uma vez na subida; valor inválido impede a API de subir.
var leitor = new LeitorAmbiente(chave => builder.Configuration[chave]);
builder.Services.AddSingleton(leitor.OpcoesIA());
// IA: LLM, RAG e Copiloto (ADR-0004, ADR-0005, ADR-0012).
builder.Services.AdicionarClienteLlm(leitor.OpcoesLlm());
builder.Services.AdicionarCopiloto(leitor.OpcoesRag());
// Login e perfis (ADR-0026): JWT em cookie httpOnly, validado pelo JwtBearer.
var opcoesSessao = leitor.OpcoesSessao();
builder.Services.AdicionarAutenticacao(opcoesSessao);

// Rate limiting do copiloto por IP (ADR-0012): protege a cota da IA.
var limiteCopiloto = leitor.RateLimitCopilotoPorMinuto();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        var correlationId = context.HttpContext.Response.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Limite de requisições excedido",
            Detail = "Você atingiu o limite de perguntas ao copiloto. Tente novamente em alguns segundos.",
            Type = "https://helpdesk.local/problemas/limite-de-requisicoes",
            Extensions = { ["codigo"] = "limite_excedido", ["correlationId"] = correlationId },
        };

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(problema, ct);
    };

    options.AddPolicy(CopilotoEndpoints.NomePoliticaRateLimit, http =>
    {
        var chaveIp = http.Connection.RemoteIpAddress?.ToString() ?? "anonimo";
        return RateLimitPartition.GetFixedWindowLimiter(chaveIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limiteCopiloto,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

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

if (opcoesSessao.ChaveGerada)
{
    LogsDeSubida.ChaveDeSessaoGerada(app.Logger);
}

// A correlação vem primeiro, para que até os erros tratados abaixo saiam com CorrelationId.
app.UseMiddleware<CorrelacaoMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Documento OpenAPI nativo + Swagger UI (só a UI) apontando para ele (ADR-0013).
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "HelpDesk API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "HelpDesk Inteligente — API";
});

app.MapSaude();
app.MapAutenticacao();
app.MapCategorias();
app.MapChamados();
app.MapTriagem();
app.MapCopiloto();
app.MapConfiguracao();
app.MapDashboard();

app.Run();

// Exposto para o WebApplicationFactory dos testes de integração.
public partial class Program;

internal static partial class LogsDeSubida
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "JWT_CHAVE não configurada: a chave das sessões foi gerada " +
        "agora e muda a cada subida (as sessões caem quando a API reinicia). Em produção, defina JWT_CHAVE.")]
    public static partial void ChaveDeSessaoGerada(ILogger logger);
}
