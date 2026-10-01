using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using HelpDesk.Application.Triagem;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Lê <c>prompts/{versao}.md</c> da pasta do executável (os arquivos são copiados no build e na imagem Docker) e
/// guarda em memória: um prompt nunca muda depois de publicado (versão nova = arquivo novo).
/// </summary>
internal sealed partial class CatalogoPromptsArquivo(string pasta) : ICatalogoPrompts
{
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _cache = new();

    public CatalogoPromptsArquivo()
        : this(Path.Combine(AppContext.BaseDirectory, "prompts"))
    {
    }

    public Task<string> ObterAsync(string versao, CancellationToken cancellationToken)
    {
        // A versão vira nome de arquivo: só letras, dígitos, ponto e hífen (nada de "../").
        if (!VersaoValida().IsMatch(versao))
        {
            throw new ArgumentException($"Versão de prompt inválida: '{versao}'.", nameof(versao));
        }

        return _cache.GetOrAdd(versao, v => new Lazy<Task<string>>(() => LerAsync(v))).Value;
    }

    private async Task<string> LerAsync(string versao)
    {
        var caminho = Path.Combine(pasta, $"{versao}.md");
        if (!File.Exists(caminho))
        {
            throw new FileNotFoundException($"O prompt '{versao}' não foi encontrado em {pasta}.", caminho);
        }

        return await File.ReadAllTextAsync(caminho);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.\-]*$")]
    private static partial Regex VersaoValida();
}
