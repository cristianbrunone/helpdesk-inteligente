using System.Globalization;
using System.Security.Cryptography;
using HelpDesk.Application.Autenticacao;

namespace HelpDesk.Infrastructure.Seguranca;

/// <summary>
/// PBKDF2-HMAC-SHA256 nativo do .NET (ADR-0026), sem pacote: 600 mil iterações (recomendação da OWASP para esse
/// algoritmo), sal aleatório de 16 bytes e hash de 32. O resultado carrega os parâmetros —
/// <c>pbkdf2-sha256$600000$&lt;sal&gt;$&lt;hash&gt;</c> em Base64 —, então aumentar as iterações no futuro não
/// invalida as senhas já gravadas. Custo medido: ~200 ms por cálculo.
/// </summary>
public sealed class HashSenhaPbkdf2 : IHashSenha
{
    public const string Algoritmo = "pbkdf2-sha256";
    public const int Iteracoes = 600_000;
    private const int TamanhoSal = 16;
    private const int TamanhoHash = 32;

    public string Gerar(string senha)
    {
        ArgumentException.ThrowIfNullOrEmpty(senha);
        var sal = RandomNumberGenerator.GetBytes(TamanhoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, sal, Iteracoes, HashAlgorithmName.SHA256, TamanhoHash);
        return string.Join('$', Algoritmo, Iteracoes.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(sal), Convert.ToBase64String(hash));
    }

    public bool Verificar(string senha, string hash)
    {
        if (string.IsNullOrEmpty(senha) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var partes = hash.Split('$');
        if (partes.Length != 4 || partes[0] != Algoritmo
            || !int.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iteracoes)
            || iteracoes <= 0)
        {
            return false;
        }

        byte[] sal, esperado;
        try
        {
            sal = Convert.FromBase64String(partes[2]);
            esperado = Convert.FromBase64String(partes[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (sal.Length == 0 || esperado.Length == 0)
        {
            return false;
        }

        var calculado = Rfc2898DeriveBytes.Pbkdf2(senha, sal, iteracoes, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }
}
