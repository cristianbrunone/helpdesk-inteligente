using System.Diagnostics;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Conhecimento;

namespace HelpDesk.Application.Conhecimento;

/// <summary>De onde vem um documento: só IDs, a categoria (desnormalizada no índice) e quando a origem mudou.</summary>
public sealed record OrigemRag(Guid? ChamadoId, Guid? ArtigoId, short? CategoriaId, DateTimeOffset AtualizadaEm)
{
    public static OrigemRag De(Chamado chamado) => new(chamado.Id, null, chamado.CategoriaId, chamado.AtualizadoEm);

    public static OrigemRag De(ArtigoConhecimento artigo) => new(null, artigo.Id, artigo.CategoriaId, artigo.AtualizadoEm);
}

/// <summary>Contagens de uma passada, para log e span (nunca conteúdo).</summary>
public sealed record ResultadoReconciliacao(int Removidos, int Sincronizados, int Indexados);

/// <summary>
/// O índice do RAG como "estado desejado × estado atual" (ADR-0010). Cada operação é atômica e segura com várias
/// instâncias do Worker ao mesmo tempo.
/// </summary>
public interface IIndiceRag
{
    /// <summary>Apaga os documentos de chamados que não estão mais resolvidos (reabertos) e de artigos inativos.</summary>
    Task<int> RemoverObsoletosAsync(CancellationToken cancellationToken);

    /// <summary>Chamados Resolvido/Fechado sem documento, ou alterados depois da última conferência.</summary>
    Task<IReadOnlyList<Chamado>> ChamadosParaConferirAsync(int limite, CancellationToken cancellationToken);

    /// <summary>Artigos ativos sem documento, ou alterados depois da última conferência.</summary>
    Task<IReadOnlyList<ArtigoConhecimento>> ArtigosParaConferirAsync(int limite, CancellationToken cancellationToken);

    /// <summary>
    /// Deixa os documentos da origem iguais aos desejados. Com os mesmos hashes, só confirma (o vetor continua
    /// valendo); senão substitui tudo, e os novos ficam sem vetor até a indexação. Devolve se o conteúdo mudou.
    /// </summary>
    Task<bool> SincronizarAsync(
        OrigemRag origem, IReadOnlyList<DocumentoParaIndexar> documentos, CancellationToken cancellationToken);

    /// <summary>
    /// Gera o vetor de até <paramref name="limite"/> documentos sem vetor ou com vetor de outro modelo
    /// (<c>FOR UPDATE SKIP LOCKED</c>: duas instâncias nunca indexam o mesmo documento). Devolve quantos indexou.
    /// </summary>
    Task<int> IndexarPendentesAsync(int limite, CancellationToken cancellationToken);
}

/// <summary>
/// Uma passada do reconciliador de indexação (ADR-0010, ADR-0011): remove o que saiu do índice, monta os documentos
/// das origens alteradas (mascarados, pelo <see cref="MontadorDocumentosRag"/>) e gera os vetores pendentes. É
/// autocorretiva: qualquer divergência (Worker parado, provedor fora, reabertura, troca de modelo) se resolve numa
/// passada seguinte, sem evento que possa se perder.
/// </summary>
public sealed class ReconciliarIndiceRag(IIndiceRag indice, MontadorDocumentosRag montador)
{
    public const string NomeFonteAtividades = "HelpDesk.Rag";

    private static readonly ActivitySource _fonte = new(NomeFonteAtividades);

    public async Task<ResultadoReconciliacao> ExecutarAsync(int lote, CancellationToken cancellationToken)
    {
        using var atividade = _fonte.StartActivity("reconciliar_indice");

        var removidos = await indice.RemoverObsoletosAsync(cancellationToken);

        var sincronizados = 0;
        foreach (var chamado in await indice.ChamadosParaConferirAsync(lote, cancellationToken))
        {
            if (await indice.SincronizarAsync(OrigemRag.De(chamado), [montador.DeChamado(chamado)], cancellationToken))
            {
                sincronizados++;
            }
        }

        foreach (var artigo in await indice.ArtigosParaConferirAsync(lote, cancellationToken))
        {
            if (await indice.SincronizarAsync(OrigemRag.De(artigo), montador.DeArtigo(artigo), cancellationToken))
            {
                sincronizados++;
            }
        }

        var indexados = await indice.IndexarPendentesAsync(lote, cancellationToken);

        atividade?.SetTag("rag.removidos", removidos);
        atividade?.SetTag("rag.sincronizados", sincronizados);
        atividade?.SetTag("rag.indexados", indexados);
        return new ResultadoReconciliacao(removidos, sincronizados, indexados);
    }
}
