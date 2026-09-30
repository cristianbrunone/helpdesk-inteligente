var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();

// Exposto para o WebApplicationFactory dos testes de integração.
public partial class Program;
