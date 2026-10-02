using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Erros;

namespace HelpDesk.Application.Chamados;

/// <summary>Corpo de <c>PATCH /api/chamados/{id}/status</c>. O comentário é opcional (P-09). O autor vem do token (ADR-0026).</summary>
public sealed record MudancaDeStatus(StatusChamado? Status, string? Comentario);

/// <summary>
/// Muda o status (RF-06). Ordem das verificações: existe (404) → versão confere (412) → dados válidos (422) →
/// regras do domínio (409). Status, histórico e comentário são gravados numa transação (RN-02).
/// </summary>
public sealed class MudarStatusChamado(
    IRepositorioChamados repositorio,
    IConsultaChamados consulta,
    TimeProvider relogio)
{
    /// <param name="versoesAceitas">Valores do <c>If-Match</c>; <c>null</c> quando o cliente não enviou precondição.</param>
    public async Task<ChamadoVersionado> ExecutarAsync(
        Guid id,
        MudancaDeStatus dados,
        string alteradoPor,
        IReadOnlyCollection<string>? versoesAceitas,
        CancellationToken cancellationToken)
    {
        var chamado = await repositorio.ObterParaAlteracaoAsync(id, cancellationToken)
            ?? throw RecursoNaoEncontradoException.Chamado(id);

        Precondicao.ExigirVersao(versoesAceitas, repositorio.Versao(chamado));

        if (dados.Status is not { } destino)
        {
            throw new ValidacaoException(new Dictionary<string, string[]>
            {
                [nameof(MudancaDeStatus.Status)] = ["Informe o novo status."],
            });
        }

        chamado.MudarStatus(destino, alteradoPor, dados.Comentario, relogio.GetUtcNow());
        await repositorio.SalvarAsync(cancellationToken);

        return await consulta.ObterDetalheAsync(id, cancellationToken)
            ?? throw RecursoNaoEncontradoException.Chamado(id);
    }
}
