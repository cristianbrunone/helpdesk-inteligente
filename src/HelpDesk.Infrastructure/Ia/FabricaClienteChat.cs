using System.ClientModel;
using System.ClientModel.Primitives;
using HelpDesk.Infrastructure.Ia.Fake;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Cria o <see cref="IChatClient"/> do provedor configurado (ADR-0005): o fake, ou o SDK da OpenAI apontado para
/// qualquer endpoint compatível (Gemini, OpenAI, Ollama...). Trocar de provedor é só variável de ambiente (NFR-09).
/// </summary>
public static class FabricaClienteChat
{
    /// <summary>
    /// O cliente que a aplicação usa: resiliência (timeout, retry, backoff) → telemetria (<c>uso_llm</c> e log, uma
    /// por tentativa) → span GenAI do OpenTelemetry (ADR-0019) → provedor. No <see cref="ChatClientBuilder"/>, o
    /// primeiro <c>Use</c> é a camada mais externa.
    /// </summary>
    public static IChatClient Montar(OpcoesLlm opcoes, ILoggerFactory logs, IRegistroUsoLlm registro) =>
        Montar(Criar(opcoes), opcoes, logs, registro);

    /// <summary>A mesma composição sobre um provedor já criado (o harness de evals põe um espião nele).</summary>
    public static IChatClient Montar(IChatClient provedor, OpcoesLlm opcoes, ILoggerFactory logs, IRegistroUsoLlm registro) =>
        new ChatClientBuilder(provedor)
            .Use(interno => new ResilienciaChatClient(interno, opcoes, logs.CreateLogger<ResilienciaChatClient>()))
            .Use(interno => new TelemetriaChatClient(interno, opcoes, registro, logs.CreateLogger<TelemetriaChatClient>()))
            // Nunca o prompt nem a resposta nos atributos: EnableSensitiveData fica desligado, explicitamente.
            .UseOpenTelemetry(logs, Observabilidade.Tracing.FonteChat, otel => otel.EnableSensitiveData = false)
            .Build();

    public static IChatClient Criar(OpcoesLlm opcoes) => opcoes.Provedor switch
    {
        TipoProvedorLlm.Fake => new FakeChatClient(opcoes.ModoFake, opcoes.AtrasoFake),
        TipoProvedorLlm.OpenAiCompativel => CriarOpenAiCompativel(opcoes),
        _ => throw new ArgumentOutOfRangeException(nameof(opcoes), opcoes.Provedor, "Provedor de LLM desconhecido."),
    };

    private static IChatClient CriarOpenAiCompativel(OpcoesLlm opcoes) =>
        ClienteOpenAi(opcoes).GetChatClient(opcoes.ModeloChat).AsIChatClient();

    /// <summary>Cliente do SDK apontado para o endpoint compatível; compartilhado com os embeddings.</summary>
    internal static OpenAIClient ClienteOpenAi(OpcoesLlm opcoes) =>
        new(
            new ApiKeyCredential(opcoes.ChaveApi!),
            new OpenAIClientOptions
            {
                Endpoint = opcoes.BaseUrl,
                // Decisão da Sprint 2: as novas tentativas ficam no nosso middleware de resiliência (uma por span,
                // com backoff e Retry-After), e não no SDK, para não multiplicar tentativas.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                // Rede de segurança: o timeout de cada tentativa é do middleware; este só evita conexão pendurada.
                NetworkTimeout = opcoes.Timeout + TimeSpan.FromSeconds(10),
            });
}
