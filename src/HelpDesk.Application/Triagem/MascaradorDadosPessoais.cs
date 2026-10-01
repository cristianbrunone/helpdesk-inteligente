using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HelpDesk.Application.Triagem;

/// <summary>
/// Único ponto que transforma texto do usuário em <see cref="TextoMascarado"/> (ADR-0006). Cobre e-mail, telefone
/// brasileiro e CPF por regex, e o nome do solicitante quando ele aparece dentro do texto. Não é um DLP completo
/// (limitação documentada no ADR-0006): na dúvida, mascara.
/// </summary>
public sealed partial class MascaradorDadosPessoais
{
    public const string MarcadorEmail = "[EMAIL]";
    public const string MarcadorTelefone = "[TELEFONE]";
    public const string MarcadorCpf = "[CPF]";
    public const string MarcadorNome = "[NOME]";

    // Partículas de nomes que, sozinhas, apareceriam em qualquer texto ("dos", "da"...).
    private static readonly HashSet<string> _particulas = ["da", "das", "de", "do", "dos", "e"];

    /// <param name="nomesConhecidos">Nomes a mascarar se aparecerem no texto (ex.: o do solicitante, RN-10).</param>
    public TextoMascarado Mascarar(string? texto, IEnumerable<string?>? nomesConhecidos = null)
    {
        var resultado = texto ?? string.Empty;
        int emails = 0, telefones = 0, cpfs = 0;

        // A ordem importa: e-mails primeiro (podem conter dígitos), depois CPF formatado, os 11 dígitos sem
        // separador (CPF ou celular) e, por último, os demais telefones.
        resultado = Email().Replace(resultado, _ => Contar(ref emails, MarcadorEmail));
        resultado = CpfFormatado().Replace(resultado, _ => Contar(ref cpfs, MarcadorCpf));
        resultado = OnzeDigitos().Replace(resultado, m => CpfValido(m.Value) || !Celular().IsMatch(m.Value)
            ? Contar(ref cpfs, MarcadorCpf)
            : Contar(ref telefones, MarcadorTelefone));
        resultado = TelefoneComDdd().Replace(resultado, _ => Contar(ref telefones, MarcadorTelefone));
        resultado = TelefoneSemDdd().Replace(resultado, _ => Contar(ref telefones, MarcadorTelefone));

        var nomes = 0;
        foreach (var parte in PartesDeNomes(nomesConhecidos))
        {
            resultado = SubstituirPalavra(resultado, parte, MarcadorNome, ref nomes);
        }

        return new TextoMascarado(resultado, new ContagemMascaramento(emails, telefones, cpfs, nomes));
    }

    private static string Contar(ref int contador, string marcador)
    {
        contador++;
        return marcador;
    }

    /// <summary>Dígitos verificadores do CPF (módulo 11). Sequências repetidas (111.111.111-11) não são válidas.</summary>
    private static bool CpfValido(string digitos)
    {
        if (digitos.Length != 11 || digitos.Distinct().Count() == 1)
        {
            return false;
        }

        int Digito(int quantidade)
        {
            var soma = 0;
            for (var i = 0; i < quantidade; i++)
            {
                soma += (digitos[i] - '0') * (quantidade + 1 - i);
            }

            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        return Digito(9) == digitos[9] - '0' && Digito(10) == digitos[10] - '0';
    }

    private static IEnumerable<string> PartesDeNomes(IEnumerable<string?>? nomes) =>
        (nomes ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .SelectMany(n => n!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(p => p.Length >= 3 && !_particulas.Contains(p.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Length);

    /// <summary>
    /// Substitui a palavra inteira ignorando caixa e acento ("João" encontra "joao"). A busca é feita numa cópia
    /// "dobrada" do texto com o mesmo comprimento, e a troca acontece no texto original.
    /// </summary>
    private static string SubstituirPalavra(string texto, string palavra, string marcador, ref int contador)
    {
        var padrao = new Regex($@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(Dobrar(palavra))}(?![\p{{L}}\p{{N}}])");
        var ocorrencias = padrao.Matches(Dobrar(texto));
        if (ocorrencias.Count == 0)
        {
            return texto;
        }

        var sb = new StringBuilder(texto);
        foreach (var ocorrencia in ocorrencias.Reverse())
        {
            sb.Remove(ocorrencia.Index, ocorrencia.Length).Insert(ocorrencia.Index, marcador);
            contador++;
        }

        return sb.ToString();
    }

    private static string Dobrar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            var decomposto = c.ToString().Normalize(NormalizationForm.FormD);
            sb.Append(char.ToLower(decomposto[0], CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<!\d)\d{3}\.\d{3}\.\d{3}-\d{2}(?!\d)")]
    private static partial Regex CpfFormatado();

    [GeneratedRegex(@"(?<![\d+])\d{11}(?!\d)")]
    private static partial Regex OnzeDigitos();

    [GeneratedRegex(@"^\d{2}9\d{8}$")]
    private static partial Regex Celular();

    // +55 opcional, DDD com ou sem parênteses, 8 ou 9 dígitos, separadores opcionais: (11) 98765-4321,
    // +55 11 3456-7890, 11987654321...
    [GeneratedRegex(@"(?<!\d)(?:\+?55[\s.-]?)?(?:\(\d{2}\)|\d{2})[\s.-]?9?\d{4}[\s.-]?\d{4}(?!\d)")]
    private static partial Regex TelefoneComDdd();

    // Sem DDD só com separador (98765-4321, 3456 7890): oito dígitos seguidos costumam ser pedido ou nota fiscal.
    [GeneratedRegex(@"(?<![\d-])9?\d{4}[\s.-]\d{4}(?![\d-])")]
    private static partial Regex TelefoneSemDdd();
}
