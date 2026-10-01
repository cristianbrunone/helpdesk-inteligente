using HelpDesk.Domain.Erros;

namespace HelpDesk.Domain.Chamados;

/// <summary>RN-01 / RN-06: a transição não existe a partir do status atual (inclui o mesmo status).</summary>
public sealed class TransicaoInvalidaException(
    StatusChamado atual,
    StatusChamado destino,
    IReadOnlyList<StatusChamado> transicoesPermitidas)
    : DominioException("transicao_invalida", $"Não é possível ir de '{atual}' para '{destino}'.")
{
    public IReadOnlyList<StatusChamado> TransicoesPermitidas { get; } = transicoesPermitidas;
}

/// <summary>RN-04: Fechado e Cancelado são finais (nem status, nem comentário).</summary>
public sealed class ChamadoFinalizadoException(StatusChamado status)
    : DominioException("chamado_finalizado", $"O chamado está '{status}' e não aceita mais alterações.");

/// <summary>RN-05: chamado com prioridade Crítica não pode ser cancelado.</summary>
public sealed class CriticoNaoCancelavelException()
    : DominioException("critico_nao_cancelavel", "Chamados com prioridade Crítica não podem ser cancelados.");
