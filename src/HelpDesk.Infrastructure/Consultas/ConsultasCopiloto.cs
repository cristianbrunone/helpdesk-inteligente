using System.ComponentModel.DataAnnotations.Schema;
using HelpDesk.Application.Copiloto;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace HelpDesk.Infrastructure.Consultas;

/// <summary>
/// As leituras das ferramentas do copiloto (contrato §copiloto). As buscas vetoriais seguem a
/// <see cref="BuscaSemantica"/> da triagem: ordem pela distância de cosseno (índice HNSW #11), varredura iterativa
/// para os filtros não esvaziarem o <c>LIMIT</c>, e o limiar fora da subconsulta para não impedir o índice. O
/// histórico é projeção do EF; as métricas, SQL explícito em <c>Consultas/Sql</c> (ADR-0009).
/// </summary>
internal sealed class ConsultasCopiloto(HelpDeskDbContext db) : IConsultasCopiloto
{
    public async Task<IReadOnlyList<ChamadoSimilar>> BuscarChamadosSimilaresAsync(
        float[] vetor, string modelo, short? categoriaId, int limite, double similaridadeMinima, Guid excetoChamadoId,
        CancellationToken cancellationToken)
    {
        var consulta = new Vector(vetor);
        // Sem categoria, o filtro vira "true" pelo segundo termo: um parâmetro nulo sem tipo o Npgsql não aceita.
        var semCategoria = categoriaId is null;
        var categoria = categoriaId ?? 0;

        return await ComVarreduraIterativaAsync(() => db.Database.SqlQuery<ChamadoSimilar>($"""
            SELECT "Id", "Numero", "Titulo", "ConteudoMascarado", "Similaridade"
            FROM (SELECT c.id AS "Id", c.numero AS "Numero", c.titulo::text AS "Titulo",
                         d.conteudo_mascarado AS "ConteudoMascarado",
                         1 - (d.embedding <=> {consulta}) AS "Similaridade"
                  FROM documentos_rag d
                  JOIN chamados c ON c.id = d.chamado_id
                  WHERE d.embedding_modelo = {modelo}
                    AND c.id <> {excetoChamadoId}
                    AND (c.categoria_id = {categoria} OR {semCategoria})
                  ORDER BY d.embedding <=> {consulta}
                  LIMIT {limite}) similares
            WHERE "Similaridade" >= {similaridadeMinima}
            ORDER BY "Similaridade" DESC
            """).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<TrechoArtigo>> BuscarArtigosAsync(
        float[] vetor, string modelo, int limite, double similaridadeMinima, CancellationToken cancellationToken)
    {
        var consulta = new Vector(vetor);
        return await ComVarreduraIterativaAsync(() => db.Database.SqlQuery<TrechoArtigo>($"""
            SELECT "Id", "Titulo", "ConteudoMascarado", "Similaridade"
            FROM (SELECT a.id AS "Id", a.titulo::text AS "Titulo", d.conteudo_mascarado AS "ConteudoMascarado",
                         1 - (d.embedding <=> {consulta}) AS "Similaridade"
                  FROM documentos_rag d
                  JOIN artigos_conhecimento a ON a.id = d.artigo_id
                  WHERE d.embedding_modelo = {modelo}
                    AND a.ativo
                  ORDER BY d.embedding <=> {consulta}
                  LIMIT {limite}) trechos
            WHERE "Similaridade" >= {similaridadeMinima}
            ORDER BY "Similaridade" DESC
            """).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<HistoricoChamado?> ObterHistoricoAsync(Guid chamadoId, CancellationToken cancellationToken) =>
        await db.Chamados
            .AsNoTracking()
            .Where(c => c.Id == chamadoId)
            .Select(c => new HistoricoChamado(
                c.Numero,
                c.Status,
                c.SolicitanteNome,
                c.Historico.OrderBy(h => h.AlteradoEm)
                    .Select(h => new MudancaStatus(h.StatusAnterior, h.StatusNovo, h.AlteradoEm)).ToList(),
                c.Comentarios.OrderBy(k => k.CriadoEm)
                    .Select(k => new ComentarioHistorico(k.Texto, k.CriadoEm)).ToList()))
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<MetricasCategoria> ObterMetricasDaCategoriaAsync(
        short categoriaId, CancellationToken cancellationToken)
    {
        var linha = await db.Database
            .SqlQueryRaw<LinhaMetricas>(ArquivosSql.Ler("metricas_da_categoria"),
                new NpgsqlParameter("categoria_id", categoriaId))
            .ToListAsync(cancellationToken);
        var m = linha.Single();
        return new MetricasCategoria(m.TotalChamados, m.Resolvidos, m.TempoMedioHoras, m.Aceitas, m.Rejeitadas);
    }

    /// <summary>
    /// O <c>SET LOCAL</c> da varredura iterativa (pgvector 0.8) só vale dentro de uma transação; abre uma se quem
    /// chamou ainda não abriu.
    /// </summary>
    private async Task<List<T>> ComVarreduraIterativaAsync<T>(Func<Task<List<T>>> consulta, CancellationToken cancellationToken)
    {
        await using var propria = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await db.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.iterative_scan = strict_order", cancellationToken);

        var resultado = await consulta();
        if (propria is not null)
        {
            await propria.CommitAsync(cancellationToken);
        }

        return resultado;
    }

    private sealed record LinhaMetricas(
        [property: Column("total_chamados")] int TotalChamados,
        [property: Column("resolvidos")] int Resolvidos,
        [property: Column("tempo_medio_horas")] double? TempoMedioHoras,
        [property: Column("aceitas")] int Aceitas,
        [property: Column("rejeitadas")] int Rejeitadas);
}
