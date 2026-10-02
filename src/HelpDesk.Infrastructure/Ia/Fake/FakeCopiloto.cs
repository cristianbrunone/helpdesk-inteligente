using System.Globalization;
using System.Text;
using System.Text.Json;
using HelpDesk.Application.Copiloto;
using Microsoft.Extensions.AI;

namespace HelpDesk.Infrastructure.Ia.Fake;

/// <summary>
/// O roteiro do copiloto no fake (ADR-0004, ADR-0012): sem chave e sem rede, mas exercitando o laço real de
/// ferramentas do <c>FunctionInvokingChatClient</c>.
/// <list type="number">
/// <item>Sem resultado de ferramenta na conversa: escolhe a ferramenta pela intenção da última mensagem do atendente
/// e devolve a chamada (<see cref="FunctionCallContent"/>). Um pedido de escrita é recusado, sem ferramenta (RF-22).</item>
/// <item>Com o resultado: responde em texto, citando os chamados como <c>#numero</c> e os artigos pelo título,
/// como o prompt exige.</item>
/// </list>
/// O modo <see cref="ModoFake.VazaDados"/> põe um CPF e uma citação inventada na resposta, para testar o guardrail
/// de saída (ADR-0020). Com <see cref="ChatOptions.MaxOutputTokens"/>, a resposta é cortada como num modelo real.
/// </summary>
internal static class FakeCopiloto
{
    public const string CpfVazado = "529.982.247-25";
    public const long NumeroInventado = 999999;
    public const int TamanhoPedaco = 8;

    public const string RespostaRecusa =
        "Não posso alterar o chamado: tenho acesso somente leitura. Para mudar o status, use os botões de status " +
        "na tela do chamado; eu posso ajudar a decidir qual é a melhor ação.";

    public const string RespostaAjuda =
        "Posso buscar chamados parecidos já resolvidos, artigos da base de conhecimento, o histórico deste chamado " +
        "ou as métricas da categoria. O que você precisa?";

    private static readonly string[] _sinaisEscrita =
    [
        "mude", "mudar", "altere", "alterar", "feche", "fechar", "resolva", "cancele", "cancelar", "aceite",
        "aceitar", "rejeite", "rejeitar", "atribua", "apague", "exclua", "status para",
    ];

    private static readonly (string Ferramenta, string[] Sinais)[] _intencoes =
    [
        (FerramentasCopiloto.ObterHistoricoDoChamado.Nome, ["historico", "o que ja foi feito", "andamento", "comentario"]),
        (FerramentasCopiloto.ObterMetricasDaCategoria.Nome, ["metrica", "tempo medio", "quantos chamados", "taxa"]),
        (FerramentasCopiloto.BuscarArtigos.Nome, ["artigo", "base de conhecimento", "procedimento", "documentacao"]),
        (FerramentasCopiloto.BuscarChamadosSimilares.Nome, ["parecid", "semelhant", "ja tivemos", "similar", "casos"]),
    ];

    /// <summary>A mensagem do assistente para esta rodada: uma chamada de ferramenta ou o texto final.</summary>
    public static (ChatMessage Mensagem, ChatFinishReason Motivo) Responder(
        IList<ChatMessage> mensagens, ChatOptions opcoes, bool vazaDados)
    {
        var resultados = ResultadosDaRodada(mensagens);
        if (resultados.Count == 0)
        {
            var pedido = Normalizar(mensagens.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty);
            if (_sinaisEscrita.Any(pedido.Contains))
            {
                return Texto(RespostaRecusa, opcoes);
            }

            var ferramenta = _intencoes.FirstOrDefault(i => i.Sinais.Any(pedido.Contains)).Ferramenta;
            if (ferramenta is not null && opcoes.Tools?.Any(t => t.Name == ferramenta) == true)
            {
                return (new ChatMessage(ChatRole.Assistant,
                    [new FunctionCallContent($"chamada_{mensagens.Count}", ferramenta, Argumentos(ferramenta, mensagens))]),
                    ChatFinishReason.ToolCalls);
            }

            return Texto(vazaDados ? RespostaAjuda + Vazamento() : RespostaAjuda, opcoes);
        }

        var texto = string.Join(' ', resultados.Select(Descrever));
        return Texto(vazaDados ? texto + Vazamento() : texto, opcoes);
    }

    /// <summary>O texto em pedaços pequenos, como um stream real: um CPF de 14 caracteres sempre cruza pedaços.</summary>
    public static IEnumerable<string> Pedacos(string texto)
    {
        for (var inicio = 0; inicio < texto.Length; inicio += TamanhoPedaco)
        {
            yield return texto.Substring(inicio, Math.Min(TamanhoPedaco, texto.Length - inicio));
        }
    }

    private static string Vazamento() =>
        $" O cliente informou o CPF {CpfVazado} no atendimento anterior. Veja também o #{NumeroInventado}.";

    /// <summary>Os resultados de ferramenta depois da última mensagem do atendente (a rodada atual da pergunta).</summary>
    private static List<FunctionResultContent> ResultadosDaRodada(IList<ChatMessage> mensagens)
    {
        var ultimaDoUsuario = -1;
        for (var i = mensagens.Count - 1; i >= 0; i--)
        {
            if (mensagens[i].Role == ChatRole.User)
            {
                ultimaDoUsuario = i;
                break;
            }
        }

        return [.. mensagens.Skip(ultimaDoUsuario + 1).SelectMany(m => m.Contents.OfType<FunctionResultContent>())];
    }

    private static Dictionary<string, object?> Argumentos(string ferramenta, IList<ChatMessage> mensagens)
    {
        if (ferramenta == FerramentasCopiloto.ObterHistoricoDoChamado.Nome)
        {
            return [];
        }

        if (ferramenta == FerramentasCopiloto.ObterMetricasDaCategoria.Nome)
        {
            return new() { ["categoria"] = CategoriaDoPrompt(mensagens) ?? "Dúvida" };
        }

        // A consulta é o assunto do chamado em contexto (como um modelo faria), não a pergunta do atendente.
        var consulta = AssuntoDoPrompt(mensagens) ?? mensagens.Last(m => m.Role == ChatRole.User).Text;
        return new() { ["consulta"] = consulta, ["limite"] = 3 };
    }

    /// <summary>Uma frase por resultado, a partir do JSON que a ferramenta devolveu.</summary>
    private static string Descrever(FunctionResultContent resultado)
    {
        if (resultado.Exception is not null || Json(resultado.Result) is not { } json)
        {
            return "Não consegui consultar essa informação agora.";
        }

        if (json.ValueKind == JsonValueKind.String)
        {
            return $"A consulta não foi aceita: {json.GetString()}";
        }

        if (json.ValueKind == JsonValueKind.Array)
        {
            var itens = json.EnumerateArray().ToList();
            if (itens.Count == 0)
            {
                return "Não encontrei nada parecido na base.";
            }

            var numeros = itens.Select(i => Propriedade(i, "numero")).Where(n => n is not null).ToList();
            if (numeros.Count > 0)
            {
                var titulo = Propriedade(itens[0], "titulo");
                var outros = numeros.Skip(1).Select(n => $"#{n}").ToList();
                return $"Encontrei {numeros.Count} casos parecidos já resolvidos. O mais próximo é o #{numeros[0]} " +
                    $"(\"{titulo}\")" + (outros.Count > 0 ? $"; também vale olhar {string.Join(" e ", outros)}." : ".") +
                    $" Sugiro começar pela solução registrada no #{numeros[0]}.";
            }

            return $"O artigo \"{Propriedade(itens[0], "titulo")}\" da base de conhecimento cobre este caso; siga os " +
                "passos descritos nele.";
        }

        if (Propriedade(json, "erro") is { } erro)
        {
            return $"A consulta não foi aceita: {erro}";
        }

        if (Propriedade(json, "statusAtual") is { } status)
        {
            var mudancas = Elemento(json, "mudancas")?.GetArrayLength() ?? 0;
            var comentarios = Elemento(json, "comentarios")?.GetArrayLength() ?? 0;
            return $"Este chamado teve {mudancas} mudanças de status e {comentarios} comentários; o status atual é {status}.";
        }

        return $"Na categoria {Propriedade(json, "categoria")}, há {Propriedade(json, "totalChamados")} chamados, " +
            $"{Propriedade(json, "resolvidos")} resolvidos, com tempo médio de resolução de " +
            $"{Propriedade(json, "tempoMedioResolucaoHoras") ?? "?"} h.";
    }

    private static JsonElement? Json(object? resultado) => resultado switch
    {
        null => null,
        JsonElement elemento => elemento,
        string texto => JsonSerializer.SerializeToElement(texto),
        _ => JsonSerializer.SerializeToElement(resultado, AIJsonUtilities.DefaultOptions),
    };

    private static JsonElement? Elemento(JsonElement objeto, string nome) =>
        objeto.ValueKind == JsonValueKind.Object
            ? objeto.EnumerateObject().Where(p => p.NameEquals(nome) || string.Equals(p.Name, nome,
                StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value).FirstOrDefault()
            : null;

    private static string? Propriedade(JsonElement objeto, string nome) => Elemento(objeto, nome) switch
    {
        null => null,
        { ValueKind: JsonValueKind.Null } => null,
        { ValueKind: JsonValueKind.String } valor => valor.GetString(),
        { } valor => valor.GetRawText(),
    };

    private static (ChatMessage, ChatFinishReason) Texto(string texto, ChatOptions opcoes)
    {
        // Orçamento de saída (ADR-0021): o fake corta como um modelo real, pela mesma estimativa de tokens.
        var limite = opcoes.MaxOutputTokens * 4;
        return limite is { } caracteres && texto.Length > caracteres
            ? (new ChatMessage(ChatRole.Assistant, texto[..caracteres]), ChatFinishReason.Length)
            : (new ChatMessage(ChatRole.Assistant, texto), ChatFinishReason.Stop);
    }

    private static string? CategoriaDoPrompt(IList<ChatMessage> mensagens) => LinhaDoSistema(mensagens, "Categoria:");

    private static string? AssuntoDoPrompt(IList<ChatMessage> mensagens) => LinhaDoSistema(mensagens, "Título:");

    /// <summary>Uma linha "Rótulo: valor" do bloco do chamado no prompt de sistema (formato do copiloto.v1).</summary>
    private static string? LinhaDoSistema(IList<ChatMessage> mensagens, string rotulo) =>
        mensagens.Where(m => m.Role == ChatRole.System)
            .SelectMany(m => m.Text.Split('\n'))
            .Select(l => l.Trim())
            .Where(l => l.StartsWith(rotulo, StringComparison.Ordinal))
            .Select(l => l[rotulo.Length..].Trim())
            .FirstOrDefault(v => v.Length > 0 && v != "(sem categoria)");

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
