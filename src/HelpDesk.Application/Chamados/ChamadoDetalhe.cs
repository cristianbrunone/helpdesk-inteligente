using HelpDesk.Application.Categorias;
using HelpDesk.Domain.Chamados;

namespace HelpDesk.Application.Chamados;

/// <summary>
/// Detalhe do chamado (contrato: <c>GET /api/chamados/{id}</c>). <see cref="TransicoesPermitidas"/> e
/// <see cref="PodeComentar"/> vêm do domínio: o front só renderiza. A <c>triagem</c> entra na Sprint 2.
/// </summary>
public sealed record ChamadoDetalhe(
    Guid Id,
    long Numero,
    string Titulo,
    string Descricao,
    string SolicitanteNome,
    string SolicitanteEmail,
    CategoriaResumo? Categoria,
    Prioridade Prioridade,
    StatusChamado Status,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm,
    DateTimeOffset? ResolvidoEm,
    IReadOnlyList<StatusChamado> TransicoesPermitidas,
    bool PodeComentar,
    IReadOnlyList<ComentarioDetalhe> Comentarios,
    IReadOnlyList<HistoricoDetalhe> Historico);

public sealed record ComentarioDetalhe(Guid Id, string Autor, string Texto, DateTimeOffset CriadoEm);

public sealed record HistoricoDetalhe(
    StatusChamado? StatusAnterior,
    StatusChamado StatusNovo,
    DateTimeOffset AlteradoEm,
    string AlteradoPor);

/// <summary>
/// O detalhe e a versão atual do chamado. A <see cref="Versao"/> é opaca para a Application: vira o <c>ETag</c> na
/// API e volta no <c>If-Match</c> das escritas.
/// </summary>
public sealed record ChamadoVersionado(ChamadoDetalhe Chamado, string Versao);
