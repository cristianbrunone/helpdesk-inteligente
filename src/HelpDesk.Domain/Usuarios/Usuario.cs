using HelpDesk.Domain.Erros;

namespace HelpDesk.Domain.Usuarios;

/// <summary>
/// Enum nativo <c>perfil_usuario</c> no PostgreSQL (ADR-0026). O atendente trata os chamados; o solicitante abre os
/// seus e acompanha só os próprios.
/// </summary>
public enum PerfilUsuario
{
    Atendente,
    Solicitante,
}

/// <summary>
/// Quem entra na aplicação (ADR-0026, substitui a P-03). O e-mail é a identidade: é único, guardado em minúsculas, e
/// é por ele que um solicitante é dono de um chamado (<c>chamados.solicitante_email</c>). A senha nunca passa por
/// aqui: o domínio só guarda o hash que a Infrastructure calculou.
/// </summary>
public sealed class Usuario
{
    public const int NomeTamanhoMaximo = 120;
    public const int EmailTamanhoMaximo = 254;

    public Guid Id { get; private set; }

    public string Nome { get; private set; }

    public string Email { get; private set; }

    public PerfilUsuario Perfil { get; private set; }

    /// <summary>Hash da senha no formato do hasher (algoritmo, iterações, sal e hash); nunca a senha.</summary>
    public string SenhaHash { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    private Usuario(Guid id, string nome, string email, PerfilUsuario perfil, string senhaHash, DateTimeOffset criadoEm)
    {
        Id = id;
        Nome = nome;
        Email = email;
        Perfil = perfil;
        SenhaHash = senhaHash;
        CriadoEm = criadoEm;
    }

    public static Usuario Criar(string? nome, string? email, PerfilUsuario perfil, string senhaHash, DateTimeOffset agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(senhaHash);

        var erros = new ErrosValidacao();
        var nomeValido = erros.Texto(nameof(Nome), nome, 1, NomeTamanhoMaximo, "o nome");
        var emailValido = erros.Email(nameof(Email), email, EmailTamanhoMaximo);
        erros.LancarSeHouver();

        return new Usuario(Guid.CreateVersion7(agora), nomeValido, NormalizarEmail(emailValido), perfil, senhaHash, agora);
    }

    /// <summary>A mesma forma do e-mail em todo lugar (cadastro, login, posse do chamado): sem espaços e em minúsculas.</summary>
    public static string NormalizarEmail(string email) => email.Trim().ToLowerInvariant();
}
