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

    public static ChatOptions ParaTriagem(this ChatOptions opcoes, Guid triagemId, Guid chamadoId)
    {
        opcoes.AdditionalProperties ??= [];
        opcoes.AdditionalProperties[Operacao] = Persistencia.RegistroUsoLlm.OperacaoTriagem;
        opcoes.AdditionalProperties[TriagemId] = triagemId;
        opcoes.AdditionalProperties[ChamadoId] = chamadoId;
        return opcoes;
    }

    internal static (string? Operacao, Guid? TriagemId, Guid? ChamadoId) Ler(ChatOptions? opcoes) => (
        opcoes?.AdditionalProperties?.TryGetValue(Operacao, out var operacao) == true ? operacao as string : null,
        opcoes?.AdditionalProperties?.TryGetValue(TriagemId, out var triagem) == true ? triagem as Guid? : null,
        opcoes?.AdditionalProperties?.TryGetValue(ChamadoId, out var chamado) == true ? chamado as Guid? : null);
}
