using System.Text.Json;
using HelpDesk.Application.Triagem;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Adaptador da etapa "Completar": transforma o <see cref="PromptTriagem"/> (instruções + chamado mascarado) em
/// mensagens para o <see cref="IChatClient"/> e devolve o texto, ou a falha classificada. O <c>json_schema</c> é
/// pedido ao provedor, mas a validação própria do pipeline sempre roda (ADR-0005, NFR-05).
/// </summary>
internal sealed partial class ClienteLlmTriagem(
    IChatClient chat,
    OpcoesLlm opcoes,
    ILogger<ClienteLlmTriagem> logger) : IClienteLlmTriagem
{
    // Mesmo formato pedido no prompt; additionalProperties=false ajuda provedores com saída estruturada.
    private static readonly JsonElement _schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "categoria": { "type": "string" },
            "prioridade": { "type": "string", "enum": ["Baixa", "Media", "Alta", "Critica"] },
            "resumo": { "type": "string" },
            "respostaSugerida": { "type": "string" },
            "confianca": { "type": "number" }
          },
          "required": ["categoria", "prioridade", "resumo", "respostaSugerida", "confianca"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public string Provedor => opcoes.NomeProvedor;

    public string Modelo => opcoes.ModeloEfetivo;

    public async Task<ResultadoLlm> CompletarAsync(
        PromptTriagem prompt, ContextoTriagem contexto, CancellationToken cancellationToken)
    {
        ChatMessage[] mensagens =
        [
            new(ChatRole.System, prompt.Sistema),
            new(ChatRole.User, prompt.MensagemDoUsuario()),
        ];
        var opcoesChat = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(_schema, "triagem", "Sugestão de triagem do chamado."),
            MaxOutputTokens = opcoes.MaxTokensSaidaTriagem,
            Temperature = 0.2f,
        }.ParaTriagem(contexto.TriagemId, contexto.ChamadoId);

        try
        {
            var resposta = await chat.GetResponseAsync(mensagens, opcoesChat, cancellationToken);
            return new ResultadoLlm(resposta.Text, resposta.FinishReason == ChatFinishReason.Length,
                resposta.ModelId ?? Modelo, FalhaTipo: null);
        }
        catch (ProvedorIndisponivelException indisponivel)
        {
            return ResultadoLlm.Falha(indisponivel.Tipo, Modelo);
        }
        catch (Exception erro) when (!cancellationToken.IsCancellationRequested)
        {
            // Ex.: 401 (chave inválida), 400. Só o tipo vai para o log: a mensagem pode ter conteúdo.
            LogFalhaDefinitiva(logger, erro.GetType().Name, contexto.TriagemId);
            return ResultadoLlm.Falha("erro", Modelo);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha definitiva do provedor de IA ({TipoErro}) na triagem {TriagemId}")]
    private static partial void LogFalhaDefinitiva(ILogger logger, string tipoErro, Guid triagemId);
}
