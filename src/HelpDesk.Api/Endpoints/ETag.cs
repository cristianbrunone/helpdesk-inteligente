namespace HelpDesk.Api.Endpoints;

/// <summary>ETag forte a partir da versão opaca do chamado (contrato §1, concorrência).</summary>
internal static class ETag
{
    public static string De(string versao) => $"\"{versao}\"";
}
