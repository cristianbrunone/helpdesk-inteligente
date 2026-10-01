namespace HelpDesk.Application.Conhecimento;

/// <summary>Recuperação do RAG (ADR-0011): quantos documentos de cada tipo e a similaridade mínima para entrar.</summary>
public sealed record OpcoesRag(int TopK, double SimilaridadeMinima)
{
    public const int TopKPadrao = 3;
    public const double SimilaridadeMinimaPadrao = 0.35;
}

/// <summary>
/// Um documento recuperado. <see cref="ConteudoMascarado"/> é o texto indexado (já sem dados pessoais), que pode ir
/// para o prompt; <see cref="Titulo"/> é o da origem, para exibir ao atendente.
/// </summary>
public sealed record DocumentoRecuperado(
    string Tipo, Guid Id, long? Numero, string Titulo, string ConteudoMascarado, double Similaridade)
{
    public const string TipoChamado = "chamado";
    public const string TipoArtigo = "artigo";
}

/// <summary>
/// Busca por similaridade de cosseno no índice do RAG (ADR-0007), escondendo o pgvector da Application. Só compara
/// vetores do <paramref name="modelo"/> informado: vetores de modelos diferentes não são comparáveis.
/// </summary>
public interface IBuscaSemantica
{
    /// <summary>Até <c>TopK</c> chamados resolvidos e até <c>TopK</c> trechos de artigo, do mais parecido ao menos.</summary>
    Task<IReadOnlyList<DocumentoRecuperado>> BuscarAsync(
        float[] vetor, string modelo, OpcoesRag opcoes, CancellationToken cancellationToken);
}
