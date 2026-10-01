using HelpDesk.Application.Categorias;
using HelpDesk.Application.Chamados;

namespace HelpDesk.Api;

/// <summary>
/// Casos de uso da Application. O registro fica no host, que é quem compõe o DI (ADR-0002): assim a Application
/// segue sem nenhuma dependência de pacote.
/// </summary>
internal static class ServicosAplicacao
{
    public static IServiceCollection AdicionarCasosDeUso(this IServiceCollection services)
    {
        services.AddScoped<ListarCategorias>();
        services.AddScoped<CriarChamado>();
        services.AddScoped<ListarChamados>();
        services.AddScoped<ObterChamado>();
        services.AddScoped<MudarStatusChamado>();
        return services;
    }
}
