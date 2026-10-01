using HelpDesk.Application.Categorias;
using HelpDesk.Infrastructure.Consultas;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpDesk.Infrastructure;

public static class ServicosInfraestrutura
{
    public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<HelpDeskDbContext>(options => ConfiguracaoBanco.Configurar(options, connectionString));
        services.AddScoped<InicializadorBanco>();
        services.AddScoped<IConsultaCategorias, ConsultaCategorias>();
        return services;
    }
}
