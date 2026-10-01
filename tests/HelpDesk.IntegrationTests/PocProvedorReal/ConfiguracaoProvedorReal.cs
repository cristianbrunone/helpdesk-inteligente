namespace HelpDesk.IntegrationTests.PocProvedorReal;

/// <summary>
/// Configuração do provedor real, lida das variáveis de ambiente ou do <c>.env</c> local (ADR-0023).
/// A chave nunca é exposta: não há <c>ToString</c> nem propriedade pública com o valor.
/// </summary>
internal sealed class ConfiguracaoProvedorReal
{
    private ConfiguracaoProvedorReal(string baseUrl, string chave, string modeloChat, string modeloEmbedding,
        int dimensoes)
    {
        BaseUrl = baseUrl;
        Chave = chave;
        ModeloChat = modeloChat;
        ModeloEmbedding = modeloEmbedding;
        Dimensoes = dimensoes;
    }

    public string BaseUrl { get; }

    internal string Chave { get; }

    public string ModeloChat { get; }

    public string ModeloEmbedding { get; }

    public int Dimensoes { get; }

    public override string ToString() => $"{BaseUrl} | chat: {ModeloChat} | embedding: {ModeloEmbedding}";

    /// <summary>Devolve <c>null</c> se faltar alguma variável: o teste é pulado, e não reprovado.</summary>
    public static ConfiguracaoProvedorReal? Carregar()
    {
        var doArquivo = LerDotEnv();
        string? Ler(string nome) =>
            Environment.GetEnvironmentVariable(nome) is { Length: > 0 } valor ? valor
            : doArquivo.TryGetValue(nome, out var doEnv) && doEnv.Length > 0 ? doEnv
            : null;

        var (url, chave, chat, embedding) =
            (Ler("LLM_BASE_URL"), Ler("LLM_API_KEY"), Ler("LLM_CHAT_MODEL"), Ler("LLM_EMBEDDING_MODEL"));
        if (url is null || chave is null || chat is null || embedding is null)
        {
            return null;
        }

        var dimensoes = int.TryParse(Ler("EMBEDDING_DIMENSIONS"), out var d) ? d : 768;
        return new ConfiguracaoProvedorReal(url, chave, chat, embedding, dimensoes);
    }

    private static Dictionary<string, string> LerDotEnv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HelpDesk.slnx")))
        {
            dir = dir.Parent;
        }

        var arquivo = dir is null ? null : Path.Combine(dir.FullName, ".env");
        if (arquivo is null || !File.Exists(arquivo))
        {
            return [];
        }

        return File.ReadAllLines(arquivo)
            .Select(linha => linha.Trim())
            .Where(linha => linha.Length > 0 && !linha.StartsWith('#') && linha.Contains('='))
            .Select(linha => linha.Split('=', 2))
            .ToDictionary(partes => partes[0].Trim(), partes => partes[1].Trim());
    }
}
