using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Conhecimento;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>
/// O índice do RAG em <c>documentos_rag</c> (ADR-0007, ADR-0010, ADR-0011). As consultas de conferência comparam a
/// origem com o documento no banco; a indexação reserva um lote com <c>FOR UPDATE SKIP LOCKED</c> e mantém a
/// transação aberta só durante a geração dos vetores desse lote.
/// </summary>
internal sealed class IndiceRag(
    HelpDeskDbContext db,
    IGeradorEmbeddings gerador,
    MascaradorDadosPessoais mascarador,
    TimeProvider relogio) : IIndiceRag
{
    public async Task<int> RemoverObsoletosAsync(CancellationToken cancellationToken)
    {
        // Reaberto (ou cancelado depois de resolvido, se um dia for possível): sai do índice.
        var chamados = await db.DocumentosRag
            .Where(d => d.ChamadoId != null && !db.Chamados.Any(c => c.Id == d.ChamadoId
                && (c.Status == StatusChamado.Resolvido || c.Status == StatusChamado.Fechado)))
            .ExecuteDeleteAsync(cancellationToken);
        var artigos = await db.DocumentosRag
            .Where(d => d.ArtigoId != null && !db.Artigos.Any(a => a.Id == d.ArtigoId && a.Ativo))
            .ExecuteDeleteAsync(cancellationToken);
        return chamados + artigos;
    }

    public async Task<IReadOnlyList<Chamado>> ChamadosParaConferirAsync(int limite, CancellationToken cancellationToken) =>
        await db.Chamados
            .AsNoTracking()
            .Include(c => c.Comentarios)
            .Where(c => c.Status == StatusChamado.Resolvido || c.Status == StatusChamado.Fechado)
            .Where(c => !db.DocumentosRag.Any(d => d.ChamadoId == c.Id)
                || db.DocumentosRag.Any(d => d.ChamadoId == c.Id && d.OrigemAtualizadaEm < c.AtualizadoEm))
            .OrderBy(c => c.AtualizadoEm)
            .Take(limite)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ArtigoConhecimento>> ArtigosParaConferirAsync(
        int limite, CancellationToken cancellationToken) =>
        await db.Artigos
            .AsNoTracking()
            .Where(a => a.Ativo)
            .Where(a => !db.DocumentosRag.Any(d => d.ArtigoId == a.Id)
                || db.DocumentosRag.Any(d => d.ArtigoId == a.Id && d.OrigemAtualizadaEm < a.AtualizadoEm))
            .OrderBy(a => a.AtualizadoEm)
            .Take(limite)
            .ToListAsync(cancellationToken);

    public async Task<bool> SincronizarAsync(
        OrigemRag origem, IReadOnlyList<DocumentoParaIndexar> documentos, CancellationToken cancellationToken)
    {
        // Uma instância por origem de cada vez: duas inserindo os mesmos chunks travariam as entradas do índice
        // único em ordens diferentes (o EF ordena os INSERTs pela chave, e os Guids v7 do mesmo milissegundo não têm
        // ordem garantida) e entrariam em deadlock. A segunda espera, encontra os mesmos hashes e só confirma.
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);
        var chave = (origem.ChamadoId ?? origem.ArtigoId)!.Value;
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({chave.ToString()}, 0))", cancellationToken);

        var existentes = await db.DocumentosRag
            .Where(d => d.ChamadoId == origem.ChamadoId && d.ArtigoId == origem.ArtigoId)
            .OrderBy(d => d.ChunkIndice)
            .ToListAsync(cancellationToken);

        var mudou = !existentes.Select(d => d.HashConteudo).SequenceEqual(documentos.Select(d => d.Hash));
        if (mudou)
        {
            var agora = relogio.GetUtcNow();
            db.DocumentosRag.RemoveRange(existentes);
            db.DocumentosRag.AddRange(documentos.Select(d => origem.ChamadoId is { } chamadoId
                ? DocumentoRag.DeChamado(chamadoId, d.Conteudo.Valor, d.Hash, origem.CategoriaId, origem.AtualizadaEm, agora)
                : DocumentoRag.DeArtigo(origem.ArtigoId!.Value, d.ChunkIndice, d.Conteudo.Valor, d.Hash,
                    origem.CategoriaId, origem.AtualizadaEm, agora)));
        }
        else
        {
            existentes.ForEach(d => d.Confirmar(origem.CategoriaId, origem.AtualizadaEm));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return mudou;
        }
        catch (DbUpdateException erro) when (erro.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation,
        })
        {
            // A origem foi apagada no meio (FK), ou o documento sumiu: a próxima passada confere.
            db.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<int> IndexarPendentesAsync(int limite, CancellationToken cancellationToken)
    {
        var modelo = gerador.Modelo;
        await using var transacao = await db.Database.BeginTransactionAsync(cancellationToken);

        // Sem vetor (fila, índice 12) ou com vetor de outro modelo (troca de LLM_EMBEDDING_MODEL ou de provedor).
        var pendentes = await db.DocumentosRag
            .FromSql($"""
                SELECT * FROM documentos_rag
                WHERE embedding IS NULL OR embedding_modelo <> {modelo}
                ORDER BY indexado_em NULLS FIRST, id
                LIMIT {limite}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        if (pendentes.Count == 0)
        {
            return 0;
        }

        // O conteúdo gravado já é mascarado; a nova passada só o devolve ao tipo que o gerador aceita (RN-11).
        var vetores = await gerador.GerarAsync(
            [.. pendentes.Select(d => mascarador.Mascarar(d.ConteudoMascarado))], cancellationToken);

        var agora = relogio.GetUtcNow();
        for (var i = 0; i < pendentes.Count; i++)
        {
            pendentes[i].Indexar(new Vector(vetores[i]), modelo, agora);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return pendentes.Count;
    }
}
