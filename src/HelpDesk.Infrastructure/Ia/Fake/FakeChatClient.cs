using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HelpDesk.Infrastructure.Ia.Fake;

/// <summary>
/// Provedor padrão (ADR-0005): roda sem chave e sem rede, e é determinístico. Lê as categorias válidas do próprio
/// prompt de sistema e escolhe categoria e prioridade por palavras-chave do chamado (já mascarado). A resposta é
/// um JSON como o de um modelo real, então parsing e validação do pipeline rodam de verdade. Os modos de falha
/// (<see cref="ModoFake"/>) simulam provedor lento, saída inválida, categoria inexistente e rate limit.
/// </summary>
public sealed class FakeChatClient(ModoFake modo = ModoFake.Normal, TimeSpan? atraso = null) : IChatClient
{
    private static readonly ChatClientMetadata _metadados =
        new(OpcoesLlm.NomeProvedorFake, providerUri: null, OpcoesLlm.ModeloFake);

    // Palavras-chave por categoria (comparadas sem acento e em minúsculas), na ordem de desempate.
    private static readonly (string Categoria, string[] Palavras)[] _palavrasPorCategoria =
    [
        ("financeiro", ["boleto", "pagamento", "nota fiscal", "cobranca", "fatura", "estorno", "financeiro"]),
        ("acesso/login", ["senha", "login", "acesso", "autenticacao", "2fa", "403"]),
        ("infraestrutura",
            ["lento", "vpn", "impressora", "servidor", "rede", "certificado", "pasta compartilhada", "fora do ar",
             "indisponivel"]),
        ("bug no sistema", ["erro", "trava", "bug", "falha", "nao salva", "exportar", "fecha sozinho"]),
        ("duvida", ["como", "duvida", "onde", "posso", "qual o prazo", "gostaria de saber"]),
    ];

    private static readonly string[] _sinaisCritica =
        ["fora do ar", "todos os usuarios", "toda a equipe", "vazamento", "parado", "nao funciona para ninguem"];

    private static readonly string[] _sinaisAlta = ["nao consigo", "impede", "bloqueado", "urgente", "nao abre"];

    private static readonly string[] _sinaisBaixa = ["como", "duvida", "gostaria de saber", "orientacao"];

    private readonly TimeSpan _atraso = atraso ?? TimeSpan.FromSeconds(30);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var mensagens = messages.ToList();
        switch (modo)
        {
            case ModoFake.Lento:
                await Task.Delay(_atraso, cancellationToken);
                break;
            case ModoFake.RateLimit:
                throw new ProvedorIndisponivelException(ProvedorIndisponivelException.TipoRateLimit,
                    "Fake em modo rate_limit (HTTP 429).", TimeSpan.FromMilliseconds(200));
        }

        var sistema = string.Join('\n', mensagens.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
        var usuario = string.Join('\n', mensagens.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        var texto = modo switch
        {
            ModoFake.JsonInvalido => "Desculpe, não consegui classificar este chamado.",
            _ => Responder(sistema, usuario, categoriaInexistente: modo == ModoFake.CategoriaInexistente),
        };

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, texto))
        {
            ModelId = OpcoesLlm.ModeloFake,
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails
            {
                InputTokenCount = EstimarTokens(sistema) + EstimarTokens(usuario),
                OutputTokenCount = EstimarTokens(texto),
            },
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resposta = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var atualizacao in resposta.ToChatResponseUpdates())
        {
            yield return atualizacao;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? _metadados
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    private static string SoOChamado(string usuario)
    {
        var inicio = usuario.IndexOf("<chamado>", StringComparison.Ordinal);
        return inicio < 0 ? usuario : usuario[inicio..];
    }

    private static string Responder(string sistema, string usuario, bool categoriaInexistente)
    {
        var categorias = CategoriasDoPrompt(sistema);
        // Só o chamado decide: as palavras dos trechos do <contexto> (RAG) não podem mudar a classificação do fake.
        usuario = SoOChamado(usuario);
        var normalizado = Normalizar(usuario);
        var (categoria, acertos) = EscolherCategoria(categorias, normalizado);

        var resposta = new
        {
            categoria = categoriaInexistente ? "Recursos Humanos" : categoria,
            prioridade = EscolherPrioridade(normalizado),
            resumo = Resumir(usuario),
            respostaSugerida =
                $"Olá! Recebemos o seu chamado e ele foi encaminhado para a equipe de {categoria}. " +
                "Se puder, envie mais detalhes ou um print do erro para agilizar a análise.",
            confianca = Math.Round(0.55 + (0.1 * Math.Min(acertos, 3)), 2),
        };
        return JsonSerializer.Serialize(resposta);
    }

    /// <summary>As linhas "- Nome" do prompt de sistema (o marcador {{CATEGORIAS}} já substituído).</summary>
    private static List<string> CategoriasDoPrompt(string sistema) =>
        [.. sistema.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..].Trim())];

    private static (string Categoria, int Acertos) EscolherCategoria(List<string> categorias, string texto)
    {
        var melhor = _palavrasPorCategoria
            .Select(p => (p.Categoria, Acertos: p.Palavras.Count(texto.Contains)))
            .Where(p => p.Acertos > 0)
            .OrderByDescending(p => p.Acertos)
            .FirstOrDefault();

        var escolhida = categorias.FirstOrDefault(c => Normalizar(c) == melhor.Categoria);
        return escolhida is not null
            ? (escolhida, melhor.Acertos)
            : (categorias.FirstOrDefault() ?? "Dúvida", 0);
    }

    private static string EscolherPrioridade(string texto) =>
        _sinaisCritica.Any(texto.Contains) ? "Critica"
        : _sinaisAlta.Any(texto.Contains) ? "Alta"
        : _sinaisBaixa.Any(texto.Contains) ? "Baixa"
        : "Media";

    /// <summary>A primeira frase da descrição (ou o título), limitada a 200 caracteres.</summary>
    private static string Resumir(string usuario)
    {
        string? Linha(string prefixo) => usuario.Split('\n')
            .FirstOrDefault(l => l.StartsWith(prefixo, StringComparison.Ordinal))?[prefixo.Length..].Trim();

        var descricao = Linha("Descrição:");
        var frase = descricao?.Split(['.', '!', '?'], 2)[0].Trim();
        var resumo = string.IsNullOrWhiteSpace(frase) ? Linha("Título:") ?? "Chamado sem descrição." : $"{frase}.";
        return resumo.Length <= 200 ? resumo : $"{resumo[..197]}...";
    }

    private static int EstimarTokens(string texto) => (texto.Length + 3) / 4;

    private static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }
}
