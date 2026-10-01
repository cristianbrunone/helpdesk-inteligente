using HelpDesk.Infrastructure.Ia.Fake;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Cria o gerador de embeddings do provedor configurado (ADR-0005, ADR-0011), com a mesma composição do chat:
/// resiliência → telemetria (<c>uso_llm</c>) → span GenAI do OpenTelemetry → provedor (fake ou OpenAI-compatível).
/// </summary>
public static class FabricaGeradorEmbeddings
{
    public static IEmbeddingGenerator<string, Embedding<float>> Montar(
        OpcoesLlm opcoes, ILoggerFactory logs, IRegistroUsoLlm registro) =>
        new EmbeddingGeneratorBuilder<string, Embedding<float>>(Criar(opcoes))
            .Use(interno => new ResilienciaEmbeddingGenerator(interno, opcoes,
                logs.CreateLogger<ResilienciaEmbeddingGenerator>()))
            .Use(interno => new TelemetriaEmbeddingGenerator(interno, opcoes, registro,
                logs.CreateLogger<TelemetriaEmbeddingGenerator>()))
            // Nunca os textos nos atributos: EnableSensitiveData fica desligado, explicitamente.
            .UseOpenTelemetry(logs, Observabilidade.Tracing.FonteEmbeddings, otel => otel.EnableSensitiveData = false)
            .Build();

    public static IEmbeddingGenerator<string, Embedding<float>> Criar(OpcoesLlm opcoes) => opcoes.Provedor switch
    {
        TipoProvedorLlm.Fake => new FakeEmbeddingGenerator(),
        TipoProvedorLlm.OpenAiCompativel => FabricaClienteChat.ClienteOpenAi(opcoes)
            .GetEmbeddingClient(opcoes.ModeloEmbedding)
            .AsIEmbeddingGenerator(),
        _ => throw new ArgumentOutOfRangeException(nameof(opcoes), opcoes.Provedor, "Provedor de LLM desconhecido."),
    };
}
