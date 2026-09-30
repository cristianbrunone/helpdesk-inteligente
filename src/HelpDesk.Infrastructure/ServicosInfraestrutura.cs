using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Infrastructure;

public static class ServicosInfraestrutura
{
    public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<HelpDeskDbContext>(options => ConfiguracaoBanco.Configurar(options, connectionString));
        services.AddScoped<InicializadorBanco>();
        return services;
    }
}
