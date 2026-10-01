using HelpDesk.Application.Triagem;
using HelpDesk.Infrastructure;
using HelpDesk.Infrastructure.Ia;

namespace HelpDesk.Worker;

/// <summary>Composição do Worker (ADR-0002: o host monta o DI). Reutilizada pelos testes de integração.</summary>
public static class ServicosWorker
{
    public static IServiceCollection AdicionarTriagem(
        this IServiceCollection services,
        string connectionString,
        OpcoesIA opcoesIA,
        OpcoesLlm opcoesLlm,
        OpcoesFila opcoesFila)
    {
        services.AdicionarInfraestrutura(connectionString);
        services.AdicionarClienteLlm(opcoesLlm);

        services.AddSingleton(opcoesIA);
        services.AddSingleton(opcoesFila);
        services.AddSingleton<MascaradorDadosPessoais>();
        services.AddSingleton<MontadorPromptTriagem>();
        services.AddScoped<PipelineTriagem>();
        services.AddScoped<ProcessarTriagemPendente>();
        services.AddSingleton<ConsumidorFilaTriagem>();
        services.AddHostedService(sp => sp.GetRequiredService<ConsumidorFilaTriagem>());
        return services;
    }
}
