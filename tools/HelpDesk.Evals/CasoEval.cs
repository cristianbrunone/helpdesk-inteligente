using System.Text.Json;

namespace HelpDesk.Evals;

/// <summary>Um caso do conjunto rotulado (<c>evals/triagem/casos.jsonl</c>; formato no LEIAME da pasta).</summary>
internal sealed record CasoEval(
    string Id,
    string Grupo,
    bool HeldOut,
    string Titulo,
    string Descricao,
    string SolicitanteNome,
    string SolicitanteEmail,
    string[] Categorias,
    string Prioridade,
    string? PrioridadeProibida = null,
    string[]? DadosPessoais = null)
{
    public const string GrupoInjecao = "injecao";
    public const string GrupoPii = "pii";

    /// <summary>Casos de segurança têm critério próprio de aprovação (ADR-0018).</summary>
    public bool Seguranca => Grupo is GrupoInjecao or GrupoPii;

    public bool CategoriaCerta(string? categoria) => categoria is not null && Categorias.Contains(categoria);
}

internal static class ConjuntoEval
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Lê o JSONL (uma linha por caso), recusando IDs repetidos ou casos sem categoria.</summary>
    public static IReadOnlyList<CasoEval> Carregar(string caminho)
    {
        var casos = File.ReadLines(caminho)
            .Where(linha => !string.IsNullOrWhiteSpace(linha))
            .Select(linha => JsonSerializer.Deserialize<CasoEval>(linha, _json)
                ?? throw new InvalidDataException($"Linha inválida em {caminho}."))
            .ToList();

        var repetido = casos.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (repetido is not null)
        {
            throw new InvalidDataException($"O caso {repetido.Key} aparece mais de uma vez em {caminho}.");
        }

        var semCategoria = casos.FirstOrDefault(c => c.Categorias is not { Length: > 0 });
        return semCategoria is null
            ? casos
            : throw new InvalidDataException($"O caso {semCategoria.Id} não tem categoria esperada.");
    }
}
