using HelpDesk.Domain.Erros;
using HelpDesk.Domain.Usuarios;

namespace HelpDesk.Application.Autenticacao;

/// <summary>Quem está na sessão: vai para o token e volta em <c>GET /api/auth/eu</c>. Nunca a senha nem o hash.</summary>
public sealed record UsuarioAutenticado(Guid Id, string Nome, string Email, PerfilUsuario Perfil);

/// <summary>Porta de leitura dos usuários (ADR-0026).</summary>
public interface IConsultaUsuarios
{
    /// <summary>Pelo e-mail já normalizado (<see cref="Usuario.NormalizarEmail"/>); nulo se não existir.</summary>
    Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken cancellationToken);
}

/// <summary>
/// Sem sessão, ou credenciais que não conferem (401 <c>nao_autenticado</c>, contrato §2). A mensagem é a mesma para
/// e-mail inexistente e senha errada: a resposta não revela quais e-mails existem.
/// </summary>
public sealed class NaoAutenticadoException(string mensagem = NaoAutenticadoException.MensagemPadrao)
    : DominioException("nao_autenticado", mensagem)
{
    public const string MensagemPadrao = "E-mail ou senha incorretos.";
}

/// <summary>
/// Login (ADR-0026): confere e-mail e senha e devolve quem entrou. Emitir o token é da API (transporte HTTP).
/// <para>
/// Para um e-mail inexistente, ainda calcula um hash: sem isso, a resposta rápida denunciaria que o e-mail não
/// existe (enumeração por tempo de resposta), já que conferir uma senha leva ~200 ms.
/// </para>
/// </summary>
public sealed class EntrarNoSistema(IConsultaUsuarios usuarios, IHashSenha hasher)
{
    private static readonly Lock _trava = new();
    private static string? _hashFicticio;

    public async Task<UsuarioAutenticado> ExecutarAsync(string? email, string? senha, CancellationToken cancellationToken)
    {
        var erros = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(email))
        {
            erros["Email"] = ["Informe o e-mail."];
        }

        if (string.IsNullOrEmpty(senha))
        {
            erros["Senha"] = ["Informe a senha."];
        }

        if (erros.Count > 0)
        {
            throw new ValidacaoException(erros);
        }

        var usuario = await usuarios.ObterPorEmailAsync(Usuario.NormalizarEmail(email!), cancellationToken);
        var confere = hasher.Verificar(senha!, usuario?.SenhaHash ?? HashFicticio());

        return usuario is not null && confere
            ? new UsuarioAutenticado(usuario.Id, usuario.Nome, usuario.Email, usuario.Perfil)
            : throw new NaoAutenticadoException();
    }

    /// <summary>Um hash válido, no formato e com o custo do real, calculado uma vez por processo.</summary>
    private string HashFicticio()
    {
        lock (_trava)
        {
            return _hashFicticio ??= hasher.Gerar(Guid.NewGuid().ToString("N"));
        }
    }
}
