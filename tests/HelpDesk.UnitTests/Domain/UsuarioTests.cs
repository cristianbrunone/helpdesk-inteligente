using HelpDesk.Domain.Erros;
using HelpDesk.Domain.Usuarios;

namespace HelpDesk.UnitTests.Domain;

public sealed class UsuarioTests
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Criar_EmailComMaiusculasEEspacos_GuardaNormalizadoENomeAparado()
    {
        var usuario = Usuario.Criar("  Ana (suporte) ", "  Ana.Suporte@Example.COM ", PerfilUsuario.Atendente,
            "pbkdf2-sha256$1$x$y", _agora);

        usuario.Email.ShouldBe("ana.suporte@example.com");
        usuario.Nome.ShouldBe("Ana (suporte)");
        usuario.Perfil.ShouldBe(PerfilUsuario.Atendente);
        usuario.CriadoEm.ShouldBe(_agora);
    }

    [Theory]
    [InlineData(null, "ana@example.com", "Nome")]
    [InlineData("   ", "ana@example.com", "Nome")]
    [InlineData("Ana", "ana-sem-arroba", "Email")]
    [InlineData("Ana", null, "Email")]
    public void Criar_NomeOuEmailInvalido_LancaValidacaoNoCampo(string? nome, string? email, string campo)
    {
        var erro = Should.Throw<ValidacaoException>(() =>
            Usuario.Criar(nome, email, PerfilUsuario.Solicitante, "hash", _agora));

        erro.Erros.Keys.ShouldContain(campo);
    }

    [Fact]
    public void Criar_SemHash_Lanca() =>
        Should.Throw<ArgumentException>(() => Usuario.Criar("Ana", "ana@example.com", PerfilUsuario.Atendente, " ", _agora));
}
