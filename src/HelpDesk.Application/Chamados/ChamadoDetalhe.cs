using HelpDesk.Application.Categorias;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.Application.Chamados;

/// <summary>
/// Detalhe do chamado (contrato: <c>GET /api/chamados/{id}</c>). <see cref="TransicoesPermitidas"/> e
/// <see cref="PodeComentar"/> vêm do domínio: o front só renderiza. <see cref="Triagem"/> é a vigente (a mais
/// recente, P-04), ou <c>null</c> se nunca houve triagem (por exemplo, com a triagem desativada).
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
    IReadOnlyList<HistoricoDetalhe> Historico,
    TriagemDetalhe? Triagem);

/// <summary>
/// Triagem vigente no detalhe. <see cref="Erro"/> é a mensagem amigável da falha, nunca o detalhe técnico.
/// <see cref="Fontes"/> são os documentos que o RAG recuperou para a sugestão (vazia sem RAG).
/// </summary>
public sealed record TriagemDetalhe(
    Guid Id,
    StatusTriagem Status,
    CategoriaResumo? CategoriaSugerida,
    Prioridade? PrioridadeSugerida,
    string? Resumo,
    string? RespostaSugerida,
    decimal? Confianca,
    string? Modelo,
    string? PromptVersao,
    IReadOnlyList<FonteTriagem> Fontes,
    string? Erro,
    DateTimeOffset CriadoEm,
    DateTimeOffset? ConcluidaEm,
    string? DecididaPor,
    DateTimeOffset? DecididaEm,
    int TotalTriagens);

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
