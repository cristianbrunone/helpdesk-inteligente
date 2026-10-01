using HelpDesk.Application.Conhecimento;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace HelpDesk.Infrastructure.Consultas;

/// <summary>
/// Busca vetorial no <c>documentos_rag</c> (ADR-0007, ADR-0011). Duas subconsultas, uma por tipo de origem, cada
/// uma ordenada pela distância de cosseno (<c>&lt;=&gt;</c>, o operador do índice HNSW #11) com <c>LIMIT</c> próprio:
/// assim os artigos, mais numerosos em trechos, não tiram o lugar dos chamados. O limiar fica fora das subconsultas,
/// porque um filtro pela distância impediria o uso do índice.
/// </summary>
internal sealed class BuscaSemantica(HelpDeskDbContext db) : IBuscaSemantica
{
    public async Task<IReadOnlyList<DocumentoRecuperado>> BuscarAsync(
        float[] vetor, string modelo, OpcoesRag opcoes, CancellationToken cancellationToken)
    {
        var consulta = new Vector(vetor);
        var topK = opcoes.TopK;

        // Com filtros (modelo e tipo), o HNSW devolve os vizinhos e o filtro descarta parte deles; a varredura
        // iterativa do pgvector 0.8 continua buscando até completar o LIMIT, em ordem estrita de distância.
        // O SET LOCAL vale só dentro de uma transação; abre uma se quem chamou ainda não abriu.
        await using var propria = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await db.Database.ExecuteSqlRawAsync("SET LOCAL hnsw.iterative_scan = strict_order", cancellationToken);

        var documentos = await db.Database.SqlQuery<DocumentoRecuperado>($"""
            SELECT tipo AS "Tipo", id AS "Id", numero AS "Numero", titulo AS "Titulo",
                   conteudo AS "ConteudoMascarado", similaridade AS "Similaridade"
            FROM (
                (SELECT 'chamado' AS tipo, c.id, c.numero, c.titulo::text AS titulo,
                        d.conteudo_mascarado AS conteudo, 1 - (d.embedding <=> {consulta}) AS similaridade
                 FROM documentos_rag d
                 JOIN chamados c ON c.id = d.chamado_id
                 WHERE d.embedding_modelo = {modelo}
                 ORDER BY d.embedding <=> {consulta}
                 LIMIT {topK})
                UNION ALL
                (SELECT 'artigo', a.id, NULL::bigint, a.titulo::text,
                        d.conteudo_mascarado, 1 - (d.embedding <=> {consulta})
                 FROM documentos_rag d
                 JOIN artigos_conhecimento a ON a.id = d.artigo_id
                 WHERE d.embedding_modelo = {modelo}
                 ORDER BY d.embedding <=> {consulta}
                 LIMIT {topK})
            ) recuperados
            WHERE similaridade >= {opcoes.SimilaridadeMinima}
            ORDER BY similaridade DESC
            """).ToListAsync(cancellationToken);

        if (propria is not null)
        {
            await propria.CommitAsync(cancellationToken);
        }

        return documentos;
    }
}
