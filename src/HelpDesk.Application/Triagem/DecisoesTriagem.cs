using System.Diagnostics;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.Application.Triagem;

/// <summary>Corpo de <c>POST /api/chamados/{id}/triagem/aceitar</c>. Quem decide vem do token (ADR-0026).</summary>
public sealed record AceiteTriagem;

/// <summary>Corpo de <c>POST /api/chamados/{id}/triagem/rejeitar</c>. O motivo é opcional. Quem decide vem do token (ADR-0026).</summary>
public sealed record RejeicaoTriagem(string? Motivo);

/// <summary>
/// "Refazer" (RF-12): nova triagem pendente, preservando as anteriores (P-04). Não altera o chamado, então não
/// usa <c>If-Match</c>. Com a triagem desativada, 503 (ADR-0021).
/// </summary>
public sealed class RefazerTriagem(
    IRepositorioChamados chamados,
    IRepositorioTriagens triagens,
    IConsultaChamados consulta,
    OpcoesIA opcoesIA,
    TimeProvider relogio)
{
    public async Task<TriagemDetalhe> ExecutarAsync(Guid chamadoId, CancellationToken cancellationToken)
    {
        if (!opcoesIA.TriagemHabilitada)
        {
            throw IaIndisponivelException.TriagemDesativada();
        }

        var chamado = await chamados.ObterParaAlteracaoAsync(chamadoId, cancellationToken)
            ?? throw RecursoNaoEncontradoException.Chamado(chamadoId);
        var vigente = await triagens.ObterVigenteAsync(chamadoId, cancellationToken);

        triagens.Adicionar(TriagemIA.Refazer(chamado, vigente, relogio.GetUtcNow(), Activity.Current?.Id));
        await chamados.SalvarAsync(cancellationToken);

        return await consulta.ObterTriagemVigenteAsync(chamadoId, cancellationToken)
            ?? throw new InvalidOperationException("A triagem recém-criada não foi encontrada.");
    }
}

/// <summary>
/// Aceitar (RF-13) e rejeitar (RF-14) a triagem vigente. Ordem: 404 (chamado ou triagem) → 412 (<c>If-Match</c>)
/// → 422 → 409 (RN-07, RN-08). Duas decisões simultâneas sobre a mesma triagem: a segunda recebe 412.
/// </summary>
public sealed class DecidirTriagem(
    IRepositorioChamados chamados,
    IRepositorioTriagens triagens,
    IConsultaChamados consulta,
    TimeProvider relogio)
{
    public Task<ChamadoVersionado> AceitarAsync(
        Guid chamadoId, string decididaPor, IReadOnlyCollection<string>? versoesAceitas, CancellationToken ct) =>
        DecidirAsync(chamadoId, versoesAceitas, (chamado, triagem, agora) =>
            triagem.Aceitar(chamado, decididaPor, agora), ct);

    public Task<ChamadoVersionado> RejeitarAsync(
        Guid chamadoId, RejeicaoTriagem? dados, string decididaPor, IReadOnlyCollection<string>? versoesAceitas, CancellationToken ct) =>
        DecidirAsync(chamadoId, versoesAceitas, (chamado, triagem, agora) =>
            triagem.Rejeitar(chamado, decididaPor, dados?.Motivo, agora), ct);

    private async Task<ChamadoVersionado> DecidirAsync(
        Guid chamadoId,
        IReadOnlyCollection<string>? versoesAceitas,
        Action<Domain.Chamados.Chamado, TriagemIA, DateTimeOffset> decidir,
        CancellationToken cancellationToken)
    {
        var chamado = await chamados.ObterParaAlteracaoAsync(chamadoId, cancellationToken)
            ?? throw RecursoNaoEncontradoException.Chamado(chamadoId);
        var triagem = await triagens.ObterVigenteAsync(chamadoId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException("Este chamado não tem triagem.");

        Precondicao.ExigirVersao(versoesAceitas, chamados.Versao(chamado));
        decidir(chamado, triagem, relogio.GetUtcNow());
        await chamados.SalvarAsync(cancellationToken);

        return await consulta.ObterDetalheAsync(chamadoId, cancellationToken)
            ?? throw RecursoNaoEncontradoException.Chamado(chamadoId);
    }
}
