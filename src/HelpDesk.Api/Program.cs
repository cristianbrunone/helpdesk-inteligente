using HelpDesk.Api.Erros;
using HelpDesk.Api.Observabilidade;

var builder = WebApplication.CreateBuilder(args);

// Logs em JSON no stdout: configurados em appsettings.json (ADR-0016).
builder.Services.AdicionarProblemDetails();
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

app.Run();

// Exposto para o WebApplicationFactory dos testes de integração.
public partial class Program;
