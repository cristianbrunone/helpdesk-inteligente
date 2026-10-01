using System.ClientModel;
using System.ClientModel.Primitives;
using HelpDesk.Infrastructure.Ia.Fake;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Cria o <see cref="IChatClient"/> do provedor configurado (ADR-0005): o fake, ou o SDK da OpenAI apontado para
/// qualquer endpoint compatível (Gemini, OpenAI, Ollama...). Trocar de provedor é só variável de ambiente (NFR-09).
/// </summary>
public static class FabricaClienteChat
{
    public static IChatClient Criar(OpcoesLlm opcoes) => opcoes.Provedor switch
    {
        TipoProvedorLlm.Fake => new FakeChatClient(opcoes.ModoFake, opcoes.AtrasoFake),
        TipoProvedorLlm.OpenAiCompativel => CriarOpenAiCompativel(opcoes),
        _ => throw new ArgumentOutOfRangeException(nameof(opcoes), opcoes.Provedor, "Provedor de LLM desconhecido."),
    };

    private static IChatClient CriarOpenAiCompativel(OpcoesLlm opcoes)
    {
        var cliente = new OpenAIClient(
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

        return cliente.GetChatClient(opcoes.ModeloChat).AsIChatClient();
    }
}
