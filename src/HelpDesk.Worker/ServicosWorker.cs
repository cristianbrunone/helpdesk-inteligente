using HelpDesk.Application.Conhecimento;
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
        OpcoesFila opcoesFila,
        OpcoesRag? opcoesRag = null,
        string versaoPrompt = MontadorPromptTriagem.VersaoPadrao)
    {
        services.AdicionarInfraestrutura(connectionString);
        services.AdicionarClienteLlm(opcoesLlm);
        services.AdicionarRecuperacaoRag(
            opcoesRag ?? new OpcoesRag(OpcoesRag.TopKPadrao, OpcoesRag.SimilaridadeMinimaPadrao));

        services.AddSingleton(opcoesIA);
        services.AddSingleton(opcoesFila);
        services.AddSingleton<MascaradorDadosPessoais>();
        services.AddSingleton(sp => new MontadorPromptTriagem(sp.GetRequiredService<ICatalogoPrompts>(), versaoPrompt));
        services.AddScoped<PipelineTriagem>();
        services.AddScoped<ProcessarTriagemPendente>();
        services.AddSingleton<ConsumidorFilaTriagem>();
        services.AddHostedService(sp => sp.GetRequiredService<ConsumidorFilaTriagem>());
        return services;
    }

    /// <summary>
    /// Reconciliador do índice do RAG (ADR-0010). Depende do cliente de IA registrado por
    /// <see cref="AdicionarTriagem"/> (o gerador de embeddings vem junto com o de chat).
    /// </summary>
    public static IServiceCollection AdicionarIndexacao(this IServiceCollection services, OpcoesReconciliacao opcoes)
    {
        services.AdicionarIndiceRag();
        services.AddSingleton(opcoes);
        services.AddSingleton<MontadorDocumentosRag>();
        services.AddScoped<ReconciliarIndiceRag>();
        services.AddSingleton<ReconciliadorIndexacao>();
        services.AddHostedService(sp => sp.GetRequiredService<ReconciliadorIndexacao>());
        return services;
    }
}
