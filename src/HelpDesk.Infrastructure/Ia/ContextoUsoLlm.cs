using Microsoft.Extensions.AI;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Contexto que quem chama o LLM passa em <see cref="ChatOptions.AdditionalProperties"/> para o
/// <see cref="TelemetriaChatClient"/> registrar a chamada em <c>uso_llm</c>. Só IDs e a operação, nunca conteúdo.
/// </summary>
public static class ContextoUsoLlm
{
    public const string Operacao = "helpdesk.operacao";
    public const string TriagemId = "helpdesk.triagem_id";
    public const string ChamadoId = "helpdesk.chamado_id";

    /// <summary>
    /// O token do prazo da tentativa, posto pela resiliência no streaming: com ele, a telemetria distingue um
    /// timeout (falha do provedor) de o atendente ter parado o stream (cancelamento).
    /// </summary>
    internal const string Prazo = "helpdesk.prazo";

    public static ChatOptions ParaTriagem(this ChatOptions opcoes, Guid triagemId, Guid chamadoId)
    {
        opcoes.AdditionalProperties ??= [];
        opcoes.AdditionalProperties[Operacao] = Persistencia.RegistroUsoLlm.OperacaoTriagem;
        opcoes.AdditionalProperties[TriagemId] = triagemId;
        opcoes.AdditionalProperties[ChamadoId] = chamadoId;
        return opcoes;
    }

    /// <summary>Uma pergunta ao copiloto sobre o chamado (cada rodada de ferramentas é uma chamada registrada).</summary>
    public static ChatOptions ParaCopiloto(this ChatOptions opcoes, Guid chamadoId)
    {
        opcoes.AdditionalProperties ??= [];
        opcoes.AdditionalProperties[Operacao] = Persistencia.RegistroUsoLlm.OperacaoCopiloto;
        opcoes.AdditionalProperties[ChamadoId] = chamadoId;
        return opcoes;
    }

    internal static ChatOptions ComPrazo(ChatOptions? opcoes, CancellationToken prazo)
    {
        var copia = opcoes?.Clone() ?? new ChatOptions();
        copia.AdditionalProperties ??= [];
        copia.AdditionalProperties[Prazo] = prazo;
        return copia;
    }

    internal static bool PrazoEsgotado(ChatOptions? opcoes) =>
        opcoes?.AdditionalProperties?.TryGetValue(Prazo, out var prazo) == true
        && prazo is CancellationToken { IsCancellationRequested: true };

    internal static (string? Operacao, Guid? TriagemId, Guid? ChamadoId) Ler(ChatOptions? opcoes) => (
        opcoes?.AdditionalProperties?.TryGetValue(Operacao, out var operacao) == true ? operacao as string : null,
        opcoes?.AdditionalProperties?.TryGetValue(TriagemId, out var triagem) == true ? triagem as Guid? : null,
        opcoes?.AdditionalProperties?.TryGetValue(ChamadoId, out var chamado) == true ? chamado as Guid? : null);
}
