using System.Text.RegularExpressions;

namespace HelpDesk.Domain.Erros;

/// <summary>
/// Acumula os erros de todos os campos antes de lançar, para o cliente receber a lista completa num único 422.
/// </summary>
internal sealed partial class ErrosValidacao
{
    private readonly Dictionary<string, List<string>> _erros = [];

    /// <summary>
    /// Valida um texto obrigatório e devolve o valor aparado. O tamanho é contado em caracteres Unicode
    /// (runes), como o <c>char_length</c> do PostgreSQL, para a API e o <c>CHECK</c> do banco concordarem.
    /// </summary>
    /// <param name="rotulo">Com artigo e em minúsculas ("o título"), para compor as mensagens.</param>
    public string Texto(string campo, string? valor, int minimo, int maximo, string rotulo)
    {
        var aparado = valor?.Trim() ?? string.Empty;
        var tamanho = aparado.EnumerateRunes().Count();

        if (tamanho == 0)
        {
            Adicionar(campo, $"Informe {rotulo}.");
        }
        else if (tamanho < minimo || tamanho > maximo)
        {
            Adicionar(campo, minimo <= 1
                ? $"{Capitalizar(rotulo)} deve ter no máximo {maximo} caracteres."
                : $"{Capitalizar(rotulo)} deve ter entre {minimo} e {maximo} caracteres.");
        }

        return aparado;
    }

    public string Email(string campo, string? valor, int maximo)
    {
        var email = Texto(campo, valor, 1, maximo, "o e-mail");
        if (email.Length > 0 && !_erros.ContainsKey(campo) && !EmailValido(email))
        {
            Adicionar(campo, "Informe um e-mail válido.");
        }

        return email;
    }

    public void Adicionar(string campo, string mensagem)
    {
        if (!_erros.TryGetValue(campo, out var mensagens))
        {
            mensagens = [];
            _erros[campo] = mensagens;
        }

        mensagens.Add(mensagem);
    }

    public void LancarSeHouver()
    {
        if (_erros.Count > 0)
        {
            throw new ValidacaoException(_erros.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }

    // Mesma regra do CHECK de chamados.solicitante_email, reforçada pelo parser do .NET.
    private static bool EmailValido(string email) =>
        FormatoEmail().IsMatch(email)
        && System.Net.Mail.MailAddress.TryCreate(email, out var endereco)
        && endereco.Address == email;

    private static string Capitalizar(string texto) => string.Concat(char.ToUpperInvariant(texto[0]), texto[1..]);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex FormatoEmail();
}
