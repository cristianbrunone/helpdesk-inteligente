using HelpDesk.Application.Categorias;
using HelpDesk.Application.Chamados;
using HelpDesk.Application.Triagem;
using HelpDesk.Infrastructure.Consultas;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure;

public static class ServicosInfraestrutura
{
    public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<HelpDeskDbContext>(options => ConfiguracaoBanco.Configurar(options, connectionString));
        services.AddScoped<InicializadorBanco>();
        services.AddScoped<IConsultaCategorias, ConsultaCategorias>();
        services.AddScoped<IRepositorioChamados, RepositorioChamados>();
        services.AddScoped<IConsultaChamados, ConsultaChamados>();
        services.AddScoped<IRepositorioTriagens, RepositorioTriagens>();
        services.AddSingleton<ICatalogoPrompts, CatalogoPromptsArquivo>();
        return services;
    }

    /// <summary>Cliente de LLM do provedor configurado, com resiliência e telemetria (ADR-0005).</summary>
    public static IServiceCollection AdicionarClienteLlm(this IServiceCollection services, OpcoesLlm opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton<IRegistroUsoLlm, RegistroUsoLlmBanco>();
        services.AddSingleton(sp => FabricaClienteChat.Montar(
            opcoes, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<IRegistroUsoLlm>()));
        return services;
    }
}
