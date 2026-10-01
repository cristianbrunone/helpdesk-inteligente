using System.Globalization;
using System.Text;
using System.Text.Json;
using HelpDesk.Application.Categorias;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;

namespace HelpDesk.Application.Triagem;

/// <summary>Resultado da validação: a sugestão pronta para o domínio, ou o motivo da falha (técnico e amigável).</summary>
public sealed record ResultadoValidacao(SugestaoTriagem? Sugestao, string? Codigo, string? Mensagem)
{
    public bool Valida => Sugestao is not null;

    public static ResultadoValidacao Ok(SugestaoTriagem sugestao) => new(sugestao, null, null);

    public static ResultadoValidacao Falha(string codigo, string mensagem) => new(null, codigo, mensagem);
}

/// <summary>
/// Etapa "Validar" do pipeline (NFR-05): a saída do LLM é entrada não confiável. Parse tolerante → schema →
/// validação de domínio (RN-09). Nunca lança: qualquer problema vira <see cref="ResultadoValidacao.Falha"/>, e a
/// triagem fica <c>Falhou</c>. O <c>json_schema</c> pedido ao provedor não substitui esta validação.
/// </summary>
public static class ValidadorSaidaTriagem
{
    public const string FormatoInvalido = "A IA retornou uma resposta fora do formato esperado.";

    private static readonly JsonDocumentOptions _opcoesJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static ResultadoValidacao Validar(string? resposta, IReadOnlyList<CategoriaResumo> categorias)
    {
        if (ExtrairObjeto(resposta) is not { } json)
        {
            return ResultadoValidacao.Falha("json_invalido", FormatoInvalido);
        }

        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(json, _opcoesJson);
        }
        catch (JsonException)
        {
            return ResultadoValidacao.Falha("json_invalido", FormatoInvalido);
        }

        using (documento)
        {
            return ValidarCampos(documento.RootElement, categorias);
        }
    }

    private static ResultadoValidacao ValidarCampos(JsonElement raiz, IReadOnlyList<CategoriaResumo> categorias)
    {
        if (raiz.ValueKind != JsonValueKind.Object
            || Texto(raiz, "categoria") is not { } nomeCategoria
            || Texto(raiz, "prioridade") is not { } textoPrioridade
            || Texto(raiz, "resumo") is not { } resumo
            || Texto(raiz, "respostaSugerida") is not { } resposta
            || Numero(raiz, "confianca") is not { } confianca)
        {
            return ResultadoValidacao.Falha("schema", FormatoInvalido);
        }

        var categoria = categorias.FirstOrDefault(c => Normalizar(c.Nome) == Normalizar(nomeCategoria));
        if (categoria is null)
        {
            return ResultadoValidacao.Falha("categoria_inexistente", "A IA sugeriu uma categoria que não existe.");
        }

        if (ParaPrioridade(textoPrioridade) is not { } prioridade)
        {
            return ResultadoValidacao.Falha("prioridade_invalida", "A IA sugeriu uma prioridade inválida.");
        }

        if (resumo.Length == 0 || resumo.Length > TriagemIA.ResumoTamanhoMaximo)
        {
            return ResultadoValidacao.Falha("resumo_invalido",
                $"A IA gerou um resumo vazio ou com mais de {TriagemIA.ResumoTamanhoMaximo} caracteres.");
        }

        if (resposta.Length == 0)
        {
            return ResultadoValidacao.Falha("schema", FormatoInvalido);
        }

        if (confianca is < 0 or > 1)
        {
            return ResultadoValidacao.Falha("confianca_fora_da_faixa",
                "A IA informou uma confiança fora da faixa de 0 a 1.");
        }

        return ResultadoValidacao.Ok(
            new SugestaoTriagem(categoria.Id, prioridade, resumo, resposta, Math.Round(confianca, 3)));
    }

    /// <summary>
    /// Parse tolerante: aceita cercas de Markdown (```json ... ```) e texto ao redor, pegando do primeiro "{" ao
    /// último "}". O que estiver dentro ainda precisa ser JSON válido.
    /// </summary>
    private static string? ExtrairObjeto(string? resposta)
    {
        if (string.IsNullOrWhiteSpace(resposta))
        {
            return null;
        }

        var inicio = resposta.IndexOf('{', StringComparison.Ordinal);
        var fim = resposta.LastIndexOf('}');
        return inicio >= 0 && fim > inicio ? resposta[inicio..(fim + 1)] : null;
    }

    private static string? Texto(JsonElement raiz, string campo) =>
        raiz.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()!.Trim()
            : null;

    /// <summary>Número JSON, ou texto numérico ("0.8"): modelos às vezes devolvem a confiança entre aspas.</summary>
    private static decimal? Numero(JsonElement raiz, string campo)
    {
        if (!raiz.TryGetProperty(campo, out var valor))
        {
            return null;
        }

        return valor.ValueKind switch
        {
            JsonValueKind.Number when valor.TryGetDecimal(out var numero) => numero,
            JsonValueKind.String when decimal.TryParse(valor.GetString(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var numero) => numero,
            _ => null,
        };
    }

    /// <summary>"Média", "media", "MÉDIA" → Media. Só os 4 valores do enum; número não é aceito.</summary>
    private static Prioridade? ParaPrioridade(string texto) => Normalizar(texto) switch
    {
        "baixa" => Prioridade.Baixa,
        "media" => Prioridade.Media,
        "alta" => Prioridade.Alta,
        "critica" => Prioridade.Critica,
        _ => null,
    };

    private static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }
}
