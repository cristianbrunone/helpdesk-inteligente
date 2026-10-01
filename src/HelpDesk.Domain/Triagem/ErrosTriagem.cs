using HelpDesk.Domain.Erros;

namespace HelpDesk.Domain.Triagem;

/// <summary>RN-07: aceitar ou rejeitar uma triagem que não está <c>Concluida</c>.</summary>
public sealed class TriagemNaoConcluidaException(StatusTriagem status)
    : DominioException(
        "triagem_nao_concluida",
        $"A triagem está '{status}': só uma triagem concluída pode ser aceita ou rejeitada.");

/// <summary>"Refazer" quando já existe uma triagem pendente para o chamado.</summary>
public sealed class TriagemEmAndamentoException()
    : DominioException(
        "triagem_em_andamento",
        "Já existe uma triagem em andamento para este chamado. Aguarde o resultado antes de refazer.");
