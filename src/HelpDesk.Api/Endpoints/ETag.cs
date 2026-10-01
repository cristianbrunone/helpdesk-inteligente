using Microsoft.Net.Http.Headers;

namespace HelpDesk.Api.Endpoints;

/// <summary>ETag forte a partir da versão opaca do chamado (contrato §1, concorrência).</summary>
internal static class ETag
{
    public static string De(string versao) => $"\"{versao}\"";

    /// <summary>
    /// Versões do header <c>If-Match</c> (sem aspas), ou <c>null</c> sem precondição. O If-Match usa comparação
    /// forte (RFC 9110 §13.1.1): ETags fracas e valores malformados nunca conferem, então resultam em 412.
    /// </summary>
    public static IReadOnlyCollection<string>? VersoesDoIfMatch(HttpRequest requisicao)
    {
        var valores = requisicao.Headers.IfMatch;
        if (string.IsNullOrWhiteSpace(valores.ToString()))
        {
            return null;
        }

        return EntityTagHeaderValue.TryParseList(valores, out var etags)
            ? [.. etags.Where(e => !e.IsWeak).Select(e => e.Equals(EntityTagHeaderValue.Any) ? "*" : e.Tag.Value!.Trim('"'))]
            : [];
    }
}
