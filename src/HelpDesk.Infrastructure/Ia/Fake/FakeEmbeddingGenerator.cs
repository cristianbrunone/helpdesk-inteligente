using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;

namespace HelpDesk.Infrastructure.Ia.Fake;

/// <summary>
/// Embeddings determinísticos sem chave de API (ADR-0011, NFR-10): <i>feature hashing</i> dos termos do texto num
/// vetor de 768 posições, normalizado. Textos com palavras em comum ficam próximos por cosseno, então a recuperação
/// é testável de verdade, mas só captura sobreposição lexical: serve para testar o pipeline, não para medir a
/// qualidade do RAG. Não usa os modos de falha do fake de chat: eles simulam o provedor da triagem.
/// </summary>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    // Palavras sem conteúdo: sem elas, "erro no boleto" e "erro na nota" não ficam parecidos só pelo "no"/"na".
    private static readonly HashSet<string> _stopwords =
    [
        "a", "o", "as", "os", "de", "da", "do", "das", "dos", "e", "em", "no", "na", "nos", "nas", "um", "uma",
        "para", "por", "com", "que", "se", "ao", "aos", "ou", "mas", "como", "mais", "foi", "ser", "esta", "este",
        "isso", "ja", "nao", "sim", "me", "meu", "minha", "eu", "ele", "ela",
    ];

    private readonly EmbeddingGeneratorMetadata _metadados =
        new("fake", null, OpcoesLlm.ModeloEmbeddingFake, Application.Conhecimento.IGeradorEmbeddings.Dimensoes);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dimensoes = options?.Dimensions ?? Application.Conhecimento.IGeradorEmbeddings.Dimensoes;

        var tokens = 0L;
        var embeddings = new List<Embedding<float>>();
        foreach (var texto in values)
        {
            var termos = Termos(texto);
            tokens += termos.Count;
            embeddings.Add(new Embedding<float>(Vetorizar(termos, dimensoes))
            {
                ModelId = OpcoesLlm.ModeloEmbeddingFake,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings)
        {
            Usage = new UsageDetails { InputTokenCount = tokens, TotalTokenCount = tokens },
        });
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(_metadados) ? _metadados
        : serviceKey is null && serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    /// <summary>Minúsculas, sem acento, só letras e dígitos, sem stopwords e com o plural simples removido.</summary>
    internal static List<string> Termos(string texto)
    {
        var semAcento = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                semAcento.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
        }

        return
        [
            .. semAcento.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2 && !_stopwords.Contains(t))
                // "boletos" e "boleto" viram o mesmo termo; "403" e "erro" ficam como estão.
                .Select(t => t.Length > 3 && t.EndsWith('s') ? t[..^1] : t),
        ];
    }

    /// <summary>
    /// Cada termo soma no índice dado pelo seu hash (FNV-1a, estável entre execuções, ao contrário de
    /// <see cref="string.GetHashCode()"/>), com peso sublinear pela frequência. Normalizado para norma L2 = 1.
    /// </summary>
    private static float[] Vetorizar(List<string> termos, int dimensoes)
    {
        var vetor = new float[dimensoes];
        foreach (var grupo in termos.GroupBy(t => t))
        {
            var hash = Fnv1a(grupo.Key);
            vetor[hash % (uint)dimensoes] += (float)(1 + Math.Log(grupo.Count()));
        }

        var norma = Math.Sqrt(vetor.Sum(v => (double)v * v));
        if (norma == 0)
        {
            // Texto sem nenhum termo: um vetor fixo e válido (o cosseno com vetor nulo é indefinido).
            vetor[0] = 1;
            return vetor;
        }

        for (var i = 0; i < vetor.Length; i++)
        {
            vetor[i] = (float)(vetor[i] / norma);
        }

        return vetor;
    }

    private static uint Fnv1a(string termo)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(termo))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return hash;
    }
}
