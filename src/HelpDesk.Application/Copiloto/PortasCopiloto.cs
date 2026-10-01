using HelpDesk.Domain.Chamados;

namespace HelpDesk.Application.Copiloto;

/// <summary>Um chamado resolvido parecido com a consulta. O conteúdo é o do índice do RAG (já mascarado na indexação).</summary>
public sealed record ChamadoSimilar(Guid Id, long Numero, string Titulo, string ConteudoMascarado, double Similaridade);

/// <summary>Um trecho de artigo da base de conhecimento parecido com a consulta.</summary>
public sealed record TrechoArtigo(Guid Id, string Titulo, string ConteudoMascarado, double Similaridade);

/// <summary>Uma mudança de status do chamado, sem o autor (minimização: o modelo não precisa saber quem foi).</summary>
public sealed record MudancaStatus(StatusChamado? De, StatusChamado Para, DateTimeOffset Em);

/// <summary>Um comentário do chamado, sem o autor. O texto vem cru do banco: a ferramenta mascara antes de devolver.</summary>
public sealed record ComentarioHistorico(string Texto, DateTimeOffset Em);

/// <summary>
/// O histórico do chamado em contexto. O nome do solicitante vem só para o mascarador encontrá-lo no texto dos
/// comentários (RN-10); nunca entra no resultado da ferramenta.
/// </summary>
public sealed record HistoricoChamado(
    long Numero,
    StatusChamado Status,
    string SolicitanteNome,
    IReadOnlyList<MudancaStatus> Mudancas,
    IReadOnlyList<ComentarioHistorico> Comentarios);

/// <summary>Os números de uma categoria: volume, resolução e decisões sobre as sugestões da IA.</summary>
public sealed record MetricasCategoria(
    int TotalChamados,
    int Resolvidos,
    double? TempoMedioResolucaoHoras,
    int TriagensAceitas,
    int TriagensRejeitadas);

/// <summary>
/// Porta de leitura das ferramentas do copiloto (ADR-0004, contrato §copiloto). Só consultas: o copiloto não tem
/// nenhuma porta de escrita (RF-22).
/// </summary>
public interface IConsultasCopiloto
{
    /// <summary>
    /// Chamados resolvidos do índice, do mais parecido ao menos, com similaridade mínima e no máximo
    /// <paramref name="limite"/>. O próprio chamado em contexto fica de fora.
    /// </summary>
    Task<IReadOnlyList<ChamadoSimilar>> BuscarChamadosSimilaresAsync(
        float[] vetor, string modelo, short? categoriaId, int limite, double similaridadeMinima, Guid excetoChamadoId,
        CancellationToken cancellationToken);

    /// <summary>Trechos de artigos ativos do índice, do mais parecido ao menos.</summary>
    Task<IReadOnlyList<TrechoArtigo>> BuscarArtigosAsync(
        float[] vetor, string modelo, int limite, double similaridadeMinima, CancellationToken cancellationToken);

    /// <summary>Nulo se o chamado não existe.</summary>
    Task<HistoricoChamado?> ObterHistoricoAsync(Guid chamadoId, CancellationToken cancellationToken);

    Task<MetricasCategoria> ObterMetricasDaCategoriaAsync(short categoriaId, CancellationToken cancellationToken);
}
