using HelpDesk.Application.Triagem;
using HelpDesk.Infrastructure.Ia;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HelpDesk.Infrastructure.Observabilidade;

/// <summary>
/// Tracing com OpenTelemetry (ADR-0019), igual na API e no Worker. Os atributos dos nossos spans levam só IDs,
/// versões, contagens e resultados; o middleware de IA roda com <c>EnableSensitiveData</c> desligado. Sem
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, nada é registrado: sem custo e sem erro.
/// </summary>
public static class Tracing
{
    public const string VariavelEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Fonte dos spans GenAI do <c>IChatClient</c> (uma por tentativa, com modelo e tokens).</summary>
    public const string FonteChat = "HelpDesk.IA.Chat";

    /// <summary>Fonte dos spans GenAI do gerador de embeddings (um por lote enviado ao provedor).</summary>
    public const string FonteEmbeddings = "HelpDesk.IA.Embeddings";

    /// <summary>Todas as fontes de atividades da aplicação, para exportar e para os testes ouvirem.</summary>
    public static readonly string[] Fontes =
        [PipelineTriagem.NomeFonteAtividades, ResilienciaChatClient.NomeFonteAtividades, FonteChat, FonteEmbeddings];

    public static IServiceCollection AdicionarTracing(
        this IServiceCollection services,
        string nomeServico,
        Uri? endpointOtlp,
        Action<TracerProviderBuilder>? instrumentacaoDoHost = null)
    {
        if (endpointOtlp is null)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(nomeServico))
            .WithTracing(tracing =>
            {
                tracing.AddSource(Fontes)
                    .AddHttpClientInstrumentation()
                    .AddNpgsql();
                instrumentacaoDoHost?.Invoke(tracing);
                tracing.AddOtlpExporter(otlp => otlp.Endpoint = endpointOtlp);
            });
        return services;
    }
}
