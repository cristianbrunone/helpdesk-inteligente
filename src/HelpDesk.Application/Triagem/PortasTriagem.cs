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

    Task<TriagemIA?> ObterAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>A triagem vigente (a mais recente, P-04) do chamado, para alteração.</summary>
    Task<TriagemIA?> ObterVigenteAsync(Guid chamadoId, CancellationToken cancellationToken);
}

/// <summary>
/// Fila de triagem (ADR-0003, ADR-0010): a própria tabela de triagens. Reservar marca o lease e conta a tentativa;
/// duas instâncias do Worker nunca recebem a mesma triagem.
/// </summary>
public interface IFilaTriagem
{
    Task<IReadOnlyList<Guid>> ReservarAsync(int quantidade, TimeSpan lease, CancellationToken cancellationToken);
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

/// <summary>
/// O que a etapa "Recuperar" entrega: os trechos (já mascarados) que vão para o prompt, do mais parecido ao menos,
/// e as fontes (uma por documento de origem) gravadas na triagem para o atendente.
/// </summary>
public sealed record ContextoRecuperado(IReadOnlyList<FonteTriagem> Fontes, IReadOnlyList<TextoMascarado> Trechos)
{
    public static readonly ContextoRecuperado Vazio = new([], []);
}

/// <summary>Etapa "Recuperar" (RAG, ADR-0004, ADR-0007, ADR-0011).</summary>
public interface IRecuperadorContexto
{
    Task<ContextoRecuperado> RecuperarAsync(
        TextoMascarado titulo, TextoMascarado descricao, CancellationToken cancellationToken);
}
