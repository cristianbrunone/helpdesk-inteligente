using HelpDesk.Application.Categorias;
using HelpDesk.Application.Chamados;
using HelpDesk.Application.Conhecimento;
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
        services.AddScoped<IFilaTriagem, FilaTriagem>();
        services.AddSingleton<ICatalogoPrompts, CatalogoPromptsArquivo>();
        return services;
    }

    /// <summary>
    /// Índice do RAG (ADR-0010), só para quem indexa (o Worker): depende do gerador de embeddings
    /// (<see cref="AdicionarClienteLlm"/>) e do mascarador.
    /// </summary>
    public static IServiceCollection AdicionarIndiceRag(this IServiceCollection services)
    {
        services.TryAddSingleton<MascaradorDadosPessoais>();
        services.AddScoped<IIndiceRag, IndiceRag>();
        return services;
    }

    /// <summary>Cliente de LLM do provedor configurado, com resiliência e telemetria (ADR-0005).</summary>
    public static IServiceCollection AdicionarClienteLlm(this IServiceCollection services, OpcoesLlm opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton<IRegistroUsoLlm, RegistroUsoLlmBanco>();
        services.AddSingleton(sp => FabricaClienteChat.Montar(
            opcoes, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<IRegistroUsoLlm>()));
        services.AddSingleton<IClienteLlmTriagem, ClienteLlmTriagem>();
        services.AddSingleton(sp => FabricaGeradorEmbeddings.Montar(
            opcoes, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<IRegistroUsoLlm>()));
        services.AddSingleton<IGeradorEmbeddings, GeradorEmbeddings>();
        services.AddSingleton<IRecuperadorContexto, RecuperadorSemRag>();
        return services;
    }
}
