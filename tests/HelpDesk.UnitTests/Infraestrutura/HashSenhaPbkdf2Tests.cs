using System.Security.Cryptography;
using HelpDesk.Infrastructure.Seguranca;

namespace HelpDesk.UnitTests.Infraestrutura;

/// <summary>PBKDF2 nativo (ADR-0026). Um hash só é calculado uma vez por classe: custa ~200 ms.</summary>
public sealed class HashSenhaPbkdf2Tests
{
    private const string Senha = "HelpDesk@2026";
    private static readonly HashSenhaPbkdf2 _hasher = new();
    private static readonly string _hash = _hasher.Gerar(Senha);

    [Fact]
    public void Verificar_SenhaCerta_Confere() => _hasher.Verificar(Senha, _hash).ShouldBeTrue();

    [Theory]
    [InlineData("helpdesk@2026")]
    [InlineData("HelpDesk@2026 ")]
    [InlineData("")]
    public void Verificar_SenhaErrada_NaoConfere(string senha) => _hasher.Verificar(senha, _hash).ShouldBeFalse();

    [Fact]
    public void Gerar_Formato_CarregaAlgoritmoIteracoesSalEHash()
    {
        var partes = _hash.Split('$');

        partes.Length.ShouldBe(4);
        partes[0].ShouldBe("pbkdf2-sha256");
        partes[1].ShouldBe("600000");
        Convert.FromBase64String(partes[2]).Length.ShouldBe(16);
        Convert.FromBase64String(partes[3]).Length.ShouldBe(32);
        _hash.ShouldNotContain(Senha);
    }

    [Fact]
    public void Gerar_MesmaSenhaDuasVezes_SaisDiferentesEHashesDiferentes()
    {
        var outro = _hasher.Gerar(Senha);

        outro.ShouldNotBe(_hash);
        _hasher.Verificar(Senha, outro).ShouldBeTrue();
    }

    [Fact]
    public void Verificar_HashComOutrasIteracoes_UsaAsDoProprioHash()
    {
        // Um hash antigo, com menos iterações, continua valendo depois de o padrão subir.
        var sal = RandomNumberGenerator.GetBytes(16);
        var calculado = Rfc2898DeriveBytes.Pbkdf2(Senha, sal, 1_000, HashAlgorithmName.SHA256, 32);
        var antigo = $"pbkdf2-sha256$1000${Convert.ToBase64String(sal)}${Convert.ToBase64String(calculado)}";

        _hasher.Verificar(Senha, antigo).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("texto-qualquer")]
    [InlineData("bcrypt$10$abc$def")]
    [InlineData("pbkdf2-sha256$abc$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$0$AAAA$AAAA")]
    [InlineData("pbkdf2-sha256$1000$isto-nao-e-base64$AAAA")]
    [InlineData("pbkdf2-sha256$1000$AAAA$")]
    public void Verificar_HashEmFormatoDesconhecido_NaoConfereENaoLanca(string hash) =>
        _hasher.Verificar(Senha, hash).ShouldBeFalse();
}
