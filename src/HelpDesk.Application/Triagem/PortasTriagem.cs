using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.Application.Triagem;

/// <summary>
/// Porta de escrita das triagens. Compartilha a unidade de trabalho com <see cref="Chamados.IRepositorioChamados"/>:
/// o <c>SalvarAsync</c> de lá grava chamado e triagem na mesma transação (RF-02).
/// </summary>
public interface IRepositorioTriagens
{
    void Adicionar(TriagemIA triagem);
}

/// <summary>IDs da triagem em processamento, para a telemetria do LLM (nunca conteúdo).</summary>
public sealed record ContextoTriagem(Guid TriagemId, Guid ChamadoId);

/// <summary>
/// Resposta do provedor, ou a falha já classificada (<c>timeout</c>, <c>rate_limit</c>, <c>indisponivel</c>,
/// <c>erro</c>): a Application não conhece as exceções da infraestrutura.
/// </summary>
public sealed record ResultadoLlm(string? Texto, bool Truncada, string Modelo, string? FalhaTipo)
{
    public static ResultadoLlm Falha(string tipo, string modelo) => new(null, false, modelo, tipo);
}

/// <summary>Etapa "Completar" (ADR-0004). Recebe só o <see cref="PromptTriagem"/>, cujo conteúdo é mascarado.</summary>
public interface IClienteLlmTriagem
{
    string Provedor { get; }

    string Modelo { get; }

    Task<ResultadoLlm> CompletarAsync(PromptTriagem prompt, ContextoTriagem contexto, CancellationToken cancellationToken);
}

/// <summary>Etapa "Recuperar" (RAG, ADR-0004/ADR-0007). Na Sprint 2 não há fontes; a busca entra na Sprint 3.</summary>
public interface IRecuperadorContexto
{
    Task<IReadOnlyList<FonteTriagem>> RecuperarAsync(
        TextoMascarado titulo, TextoMascarado descricao, CancellationToken cancellationToken);
}
