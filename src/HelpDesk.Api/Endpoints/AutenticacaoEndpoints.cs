using System.Security.Claims;
using HelpDesk.Api.Autenticacao;
using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Usuarios;
using HelpDesk.Infrastructure.Seguranca;
using Microsoft.AspNetCore.Http.HttpResults;

namespace HelpDesk.Api.Endpoints;

/// <summary>Login, sessão atual e saída (ADR-0026, contrato §3 "Autenticação").</summary>
internal static class AutenticacaoEndpoints
{
    public static IEndpointRouteBuilder MapAutenticacao(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth").WithTags("Autenticação");

        grupo.MapPost("/login", Entrar)
            .AllowAnonymous()
            .WithName("Entrar")
            .WithSummary("Confere e-mail e senha e grava a sessão num cookie httpOnly (o token não vem no corpo).")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity);

        grupo.MapGet("/eu", (ClaimsPrincipal principal) => TypedResults.Ok(UsuarioDaSessao.De(principal.ObterUsuario())))
            .RequireAuthorization()
            .WithName("ObterSessao")
            .WithSummary("Quem está na sessão; 401 sem sessão.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        grupo.MapPost("/sair", (HttpContext http, OpcoesSessao opcoes) =>
            {
                http.Response.Cookies.Delete(ConfiguracaoAutenticacao.NomeCookie, OpcoesCookie(opcoes, expiraEm: null));
                return TypedResults.NoContent();
            })
            .AllowAnonymous()
            .WithName("Sair")
            .WithSummary("Apaga o cookie da sessão.");

        return app;
    }

    private static async Task<Ok<UsuarioDaSessao>> Entrar(
        CredenciaisLogin corpo, EntrarNoSistema entrar, EmissorToken emissor, OpcoesSessao opcoes, HttpContext http,
        CancellationToken cancellationToken)
    {
        var usuario = await entrar.ExecutarAsync(corpo.Email, corpo.Senha, cancellationToken);
        var token = emissor.Emitir(usuario);
        http.Response.Cookies.Append(ConfiguracaoAutenticacao.NomeCookie, token.Token, OpcoesCookie(opcoes, token.ExpiraEm));
        return TypedResults.Ok(UsuarioDaSessao.De(usuario));
    }

    /// <summary>
    /// <c>HttpOnly</c>: o JavaScript nunca lê o token (um XSS não o rouba). <c>SameSite=Strict</c>: o navegador só o
    /// envia em requisições da própria origem, o que basta contra CSRF com o front e a API atrás do mesmo Nginx.
    /// </summary>
    private static CookieOptions OpcoesCookie(OpcoesSessao opcoes, DateTimeOffset? expiraEm) => new()
    {
        HttpOnly = true,
        Secure = opcoes.CookieSeguro,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiraEm,
        IsEssential = true,
    };
}

internal sealed record CredenciaisLogin(string? Email, string? Senha);

/// <summary>O usuário como a API o devolve: sem token, sem hash.</summary>
internal sealed record UsuarioDaSessao(Guid Id, string Nome, string Email, PerfilUsuario Perfil)
{
    public static UsuarioDaSessao De(UsuarioAutenticado usuario) =>
        new(usuario.Id, usuario.Nome, usuario.Email, usuario.Perfil);
}
