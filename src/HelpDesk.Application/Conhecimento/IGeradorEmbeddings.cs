using HelpDesk.Application.Triagem;

namespace HelpDesk.Application.Conhecimento;

/// <summary>
/// Geração de embeddings para o RAG (ADR-0011). Recebe <see cref="TextoMascarado"/>, e não <c>string</c>: só texto
/// sem dados pessoais vira vetor (RN-11). Devolve vetores de <see cref="Dimensoes"/> posições com norma L2 = 1, na
/// mesma ordem dos textos.
/// </summary>
public interface IGeradorEmbeddings
{
    /// <summary>Dimensão fixa da coluna <c>documentos_rag.embedding</c> (ADR-0011).</summary>
    const int Dimensoes = 768;

    /// <summary>
    /// O modelo efetivo, gravado em <c>documentos_rag.embedding_modelo</c>. A busca só compara vetores deste
    /// modelo, e o reconciliador reindexa o que foi gerado por outro.
    /// </summary>
    string Modelo { get; }

    Task<IReadOnlyList<float[]>> GerarAsync(IReadOnlyList<TextoMascarado> textos, CancellationToken cancellationToken);
}
