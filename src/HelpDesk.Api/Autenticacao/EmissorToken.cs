using System.Security.Claims;
using HelpDesk.Application.Autenticacao;
using HelpDesk.Infrastructure.Seguranca;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.Api.Autenticacao;

/// <summary>Um token emitido e quando ele expira (a mesma validade vai para o cookie).</summary>
internal sealed record TokenEmitido(string Token, DateTimeOffset ExpiraEm);

/// <summary>
/// Emite o JWT da sessão (ADR-0026): HMAC-SHA256, com o id, o nome, o e-mail e o perfil. Fica na API porque o token
/// é transporte HTTP; quem confere a senha é a Application (<see cref="EntrarNoSistema"/>).
/// </summary>
internal sealed class EmissorToken(OpcoesSessao opcoes, TimeProvider relogio)
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credenciais =
        new(new SymmetricSecurityKey(opcoes.Chave), SecurityAlgorithms.HmacSha256);

    public TokenEmitido Emitir(UsuarioAutenticado usuario)
    {
        var agora = relogio.GetUtcNow();
        var expiraEm = agora + opcoes.Validade;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ConfiguracaoAutenticacao.Emissor,
            Audience = ConfiguracaoAutenticacao.Audiencia,
            IssuedAt = agora.UtcDateTime,
            NotBefore = agora.UtcDateTime,
            Expires = expiraEm.UtcDateTime,
            SigningCredentials = _credenciais,
            Subject = new ClaimsIdentity(
            [
                new Claim(ConfiguracaoAutenticacao.ClaimId, usuario.Id.ToString()),
                new Claim(ConfiguracaoAutenticacao.ClaimNome, usuario.Nome),
                new Claim(ConfiguracaoAutenticacao.ClaimEmail, usuario.Email),
                new Claim(ConfiguracaoAutenticacao.ClaimPerfil, usuario.Perfil.ToString()),
            ]),
        });
        return new TokenEmitido(token, expiraEm);
    }
}
