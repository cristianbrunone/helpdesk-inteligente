using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using Microsoft.Extensions.AI;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Adaptador da porta <see cref="IGeradorEmbeddings"/> sobre o <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// do provedor (já com resiliência e telemetria). Pede 768 dimensões, confere a resposta e normaliza cada vetor: a
/// PoC mostrou que o Gemini devolve o vetor reduzido com norma ≈ 0,59, e a busca por cosseno com HNSW assume
/// vetores comparáveis (ADR-0011).
/// </summary>
internal sealed class GeradorEmbeddings(IEmbeddingGenerator<string, Embedding<float>> gerador, OpcoesLlm opcoes)
    : IGeradorEmbeddings
{
    public string Modelo => opcoes.ModeloEmbeddingEfetivo;

    public async Task<IReadOnlyList<float[]>> GerarAsync(
        IReadOnlyList<TextoMascarado> textos, CancellationToken cancellationToken)
    {
        if (textos.Count == 0)
        {
            return [];
        }

        var resultado = await gerador.GenerateAsync(
            textos.Select(t => t.Valor),
            new EmbeddingGenerationOptions { Dimensions = IGeradorEmbeddings.Dimensoes },
            cancellationToken);

        if (resultado.Count != textos.Count)
        {
            throw new InvalidOperationException(
                $"O provedor devolveu {resultado.Count} embeddings para {textos.Count} textos.");
        }

        return [.. resultado.Select(e => Normalizar(e.Vector.Span))];
    }

    /// <summary>Copia o vetor com norma L2 = 1. Dimensão errada ou vetor nulo são erro de configuração do provedor.</summary>
    internal static float[] Normalizar(ReadOnlySpan<float> vetor)
    {
        if (vetor.Length != IGeradorEmbeddings.Dimensoes)
        {
            throw new InvalidOperationException(
                $"O provedor devolveu {vetor.Length} dimensões; a coluna de embeddings tem {IGeradorEmbeddings.Dimensoes}.");
        }

        var soma = 0d;
        foreach (var v in vetor)
        {
            soma += (double)v * v;
        }

        var norma = Math.Sqrt(soma);
        if (norma == 0 || double.IsNaN(norma))
        {
            throw new InvalidOperationException("O provedor devolveu um embedding nulo.");
        }

        var normalizado = new float[vetor.Length];
        for (var i = 0; i < vetor.Length; i++)
        {
            normalizado[i] = (float)(vetor[i] / norma);
        }

        return normalizado;
    }
}
