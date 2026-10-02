using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HelpDesk.Application.Triagem;

namespace HelpDesk.Application.Copiloto;

/// <summary>
/// O resultado da verificação de citações: as fontes que a resposta cita e que as ferramentas realmente devolveram
/// (evento <c>fontes</c>), e as referências <c>#numero</c> sem correspondência (evento <c>aviso</c>).
/// </summary>
public sealed record VerificacaoCitacoes(IReadOnlyList<FonteCopiloto> Fontes, IReadOnlyList<string> NaoVerificadas);

/// <summary>
/// Guardrail de saída do copiloto (ADR-0020, RF-23). Todo texto gerado pelo modelo passa por aqui antes de chegar
/// ao atendente:
/// <list type="bullet">
/// <item><b>PII no stream:</b> os pedaços entram num buffer que retém os últimos <see cref="RetencaoPadrao"/>
/// caracteres, para que um CPF ou e-mail dividido entre pedaços seja visto inteiro pelo
/// <see cref="MascaradorDadosPessoais"/> antes de sair. O corte prefere um espaço (e nunca parte um marcador).</item>
/// <item><b>Citações:</b> no fim, cada <c>#numero</c> citado é comparado com os chamados que as ferramentas
/// devolveram nesta resposta; artigos contam quando o título aparece no texto. Sem chamada extra ao LLM.</item>
/// </list>
/// Uma instância por resposta. Não é thread-safe: o stream é consumido em sequência.
/// </summary>
public sealed partial class FiltroSaidaCopiloto(
    MascaradorDadosPessoais mascarador,
    IEnumerable<string?>? nomesConhecidos = null,
    int retencao = FiltroSaidaCopiloto.RetencaoPadrao)
{
    public const int RetencaoPadrao = 64;

    private static readonly string[] _marcadores =
    [
        MascaradorDadosPessoais.MarcadorEmail, MascaradorDadosPessoais.MarcadorTelefone,
        MascaradorDadosPessoais.MarcadorCpf, MascaradorDadosPessoais.MarcadorNome,
    ];

    private readonly string?[] _nomes = [.. nomesConhecidos ?? []];
    private readonly StringBuilder _liberado = new();
    private string _buffer = string.Empty;
    private bool _finalizado;

    /// <summary>Quantos dados pessoais o filtro mascarou na saída: diferente de zero indica falha em outra camada.</summary>
    public ContagemMascaramento Mascaramentos { get; private set; } = ContagemMascaramento.Nenhum;

    /// <summary>Recebe um pedaço do modelo e devolve o que já pode ir ao cliente (pode ser vazio).</summary>
    public string Processar(string pedaco)
    {
        if (_finalizado)
        {
            throw new InvalidOperationException("O filtro já foi finalizado.");
        }

        // O buffer é sempre texto já mascarado; os marcadores não casam de novo, então a contagem só soma o que é novo.
        _buffer = Mascarar(_buffer + pedaco);
        if (_buffer.Length <= retencao)
        {
            return string.Empty;
        }

        var corte = PontoDeCorte(_buffer, _buffer.Length - retencao);
        return Liberar(corte);
    }

    /// <summary>Fim do stream: libera o restante do buffer, já mascarado.</summary>
    public string Finalizar()
    {
        if (_finalizado)
        {
            return string.Empty;
        }

        _finalizado = true;
        _buffer = Mascarar(_buffer);
        return Liberar(_buffer.Length);
    }

    /// <summary>
    /// Compara as citações do texto liberado com as <paramref name="fontes"/> que as ferramentas devolveram.
    /// <paramref name="numerosPermitidos"/> são números que o modelo pode citar sem ser fonte (o chamado em
    /// contexto, que está no prompt de sistema). Só depois de <see cref="Finalizar"/>.
    /// </summary>
    public VerificacaoCitacoes VerificarCitacoes(IReadOnlyList<FonteCopiloto> fontes, IEnumerable<long> numerosPermitidos)
    {
        if (!_finalizado)
        {
            throw new InvalidOperationException("Verifique as citações só depois de finalizar o stream.");
        }

        var texto = _liberado.ToString();
        var citados = Citacao().Matches(texto)
            .Select(m => long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? n
                : -1)
            .Distinct()
            .ToList();
        var permitidos = numerosPermitidos.ToHashSet();
        var chamados = fontes.Where(f => f.Tipo == FonteCopiloto.TipoChamado && f.Numero is not null)
            .ToDictionary(f => f.Numero!.Value);

        var verificadas = citados.Where(chamados.ContainsKey).Select(n => chamados[n]);
        var artigos = fontes.Where(f => f.Tipo == FonteCopiloto.TipoArtigo
            && CultureInfo.InvariantCulture.CompareInfo.IndexOf(texto, f.Titulo,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
        var naoVerificadas = citados
            .Where(n => !chamados.ContainsKey(n) && !permitidos.Contains(n))
            .Select(n => $"#{n}");

        return new VerificacaoCitacoes([.. verificadas, .. artigos], [.. naoVerificadas]);
    }

    private string Mascarar(string texto)
    {
        var mascarado = mascarador.Mascarar(texto, _nomes);
        Mascaramentos += mascarado.Mascaramentos;
        return mascarado.Valor;
    }

    private string Liberar(int corte)
    {
        var saida = _buffer[..corte];
        _buffer = _buffer[corte..];
        _liberado.Append(saida);
        return saida;
    }

    /// <summary>
    /// Até <paramref name="limite"/>: depois do último espaço (um dado pessoal sem espaço fica inteiro no buffer) ou,
    /// sem espaço, no próprio limite, recuando para não partir um marcador nem um par substituto.
    /// </summary>
    private static int PontoDeCorte(string texto, int limite)
    {
        var espaco = texto.LastIndexOfAny([' ', '\n', '\t'], limite - 1);
        if (espaco >= 0)
        {
            return espaco + 1;
        }

        var corte = limite;
        foreach (var marcador in _marcadores)
        {
            var inicio = texto.LastIndexOf(marcador, Math.Min(texto.Length - 1, corte + marcador.Length - 1),
                StringComparison.Ordinal);
            if (inicio >= 0 && inicio < corte && inicio + marcador.Length > corte)
            {
                corte = inicio;
            }
        }

        return corte > 0 && char.IsHighSurrogate(texto[corte - 1]) ? corte - 1 : corte;
    }

    // "#877" citado; não casa "C#", "issue#12" nem "##".
    [GeneratedRegex(@"(?<![\w#])#(\d{1,9})(?!\d)")]
    private static partial Regex Citacao();
}
