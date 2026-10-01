using Pgvector;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>
/// Unidade de indexação do RAG (tabela <c>documentos_rag</c>, ADR-0011): um chamado resolvido ou um chunk de artigo.
/// Guarda o conteúdo já mascarado (o que vai para o prompt) e o vetor. É registro técnico, não conceito de domínio.
/// <para>
/// O vetor é nulo até o reconciliador gerar o embedding (ADR-0010); <c>embedding</c> e <c>embedding_modelo</c> são
/// preenchidos juntos, e a busca só compara vetores do modelo configurado.
/// </para>
/// </summary>
public sealed class DocumentoRag
{
    /// <summary>Dimensão fixa da coluna (ADR-0011): o fake e o provedor real geram 768.</summary>
    public const int Dimensoes = Application.Conhecimento.IGeradorEmbeddings.Dimensoes;

    public Guid Id { get; private set; }

    public Guid? ChamadoId { get; private set; }

    public Guid? ArtigoId { get; private set; }

    public short ChunkIndice { get; private set; }

    public string ConteudoMascarado { get; private set; }

    /// <summary>SHA-256 (hex) do conteúdo mascarado: mudou o texto, muda o hash, e o documento é reindexado.</summary>
    public string HashConteudo { get; private set; }

    /// <summary>Desnormalizado da origem, para filtrar a busca por categoria sem JOIN.</summary>
    public short? CategoriaId { get; private set; }

    public Vector? Embedding { get; private set; }

    public string? EmbeddingModelo { get; private set; }

    public DateTimeOffset? IndexadoEm { get; private set; }

    /// <summary>
    /// O <c>atualizado_em</c> da origem quando este documento foi conferido. O reconciliador só recalcula o hash das
    /// origens alteradas depois disso, em vez de remontar todos os chamados a cada passada.
    /// </summary>
    public DateTimeOffset OrigemAtualizadaEm { get; private set; }

    private DocumentoRag(
        Guid id, Guid? chamadoId, Guid? artigoId, short chunkIndice, string conteudoMascarado, string hashConteudo,
        short? categoriaId, DateTimeOffset origemAtualizadaEm)
    {
        Id = id;
        ChamadoId = chamadoId;
        ArtigoId = artigoId;
        ChunkIndice = chunkIndice;
        ConteudoMascarado = conteudoMascarado;
        HashConteudo = hashConteudo;
        CategoriaId = categoriaId;
        OrigemAtualizadaEm = origemAtualizadaEm;
    }

    public static DocumentoRag DeChamado(
        Guid chamadoId, string conteudoMascarado, string hashConteudo, short? categoriaId,
        DateTimeOffset origemAtualizadaEm, DateTimeOffset agora) =>
        new(Guid.CreateVersion7(agora), chamadoId, null, 0, conteudoMascarado, hashConteudo, categoriaId,
            origemAtualizadaEm);

    public static DocumentoRag DeArtigo(
        Guid artigoId, short chunkIndice, string conteudoMascarado, string hashConteudo, short? categoriaId,
        DateTimeOffset origemAtualizadaEm, DateTimeOffset agora) =>
        new(Guid.CreateVersion7(agora), null, artigoId, chunkIndice, conteudoMascarado, hashConteudo, categoriaId,
            origemAtualizadaEm);

    /// <summary>
    /// A origem mudou, mas o conteúdo indexável não (ex.: Resolvido → Fechado, ou a categoria aceita): o vetor
    /// continua valendo, e só a categoria desnormalizada e a data de conferência são atualizadas.
    /// </summary>
    public void Confirmar(short? categoriaId, DateTimeOffset origemAtualizadaEm)
    {
        CategoriaId = categoriaId;
        OrigemAtualizadaEm = origemAtualizadaEm;
    }

    public void Indexar(Vector embedding, string modelo, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelo);
        if (embedding.Memory.Length != Dimensoes)
        {
            throw new ArgumentException($"O embedding deve ter {Dimensoes} dimensões.", nameof(embedding));
        }

        Embedding = embedding;
        EmbeddingModelo = modelo;
        IndexadoEm = agora;
    }
}
