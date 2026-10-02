using System.Security.Claims;
using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Usuarios;
using HelpDesk.Infrastructure.Seguranca;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.Api.Autenticacao;

/// <summary>
/// Autenticação da API (ADR-0026): o <c>JwtBearer</c> valida o token do cookie <see cref="NomeCookie"/> ou, sem ele,
/// do header <c>Authorization: Bearer</c> (Swagger, testes, <c>curl</c>). As respostas 401 e 403 saem como
/// ProblemDetails do contrato (§2), e não vazias.
/// </summary>
internal static class ConfiguracaoAutenticacao
{
    public const string NomeCookie = "helpdesk_sessao";
    public const string Emissor = "helpdesk-api";
    public const string Audiencia = "helpdesk";

    // Nomes curtos do JWT, sem o mapeamento para as URIs do WS-Federation (MapInboundClaims = false).
    public const string ClaimId = "sub";
    public const string ClaimNome = "name";
    public const string ClaimEmail = "email";
    public const string ClaimPerfil = "role";

    public static IServiceCollection AdicionarAutenticacao(this IServiceCollection services, OpcoesSessao opcoes)
    {
        services.AddSingleton(opcoes);
        services.AddSingleton<EmissorToken>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = Emissor,
                    ValidAudience = Audiencia,
                    IssuerSigningKey = new SymmetricSecurityKey(opcoes.Chave),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = ClaimNome,
                    RoleClaimType = ClaimPerfil,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                jwt.Events = new JwtBearerEvents
                {
                    // O header tem prioridade; sem ele, vale o cookie httpOnly gravado no login.
                    OnMessageReceived = contexto =>
                    {
                        if (string.IsNullOrEmpty(contexto.Request.Headers.Authorization))
                        {
                            contexto.Token = contexto.Request.Cookies[NomeCookie];
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async contexto =>
                    {
                        contexto.HandleResponse();
                        await EscreverProblemaAsync(contexto.HttpContext, StatusCodes.Status401Unauthorized,
                            "Entre no sistema para continuar.");
                    },
                    OnForbidden = contexto => EscreverProblemaAsync(contexto.HttpContext,
                        StatusCodes.Status403Forbidden, "O seu perfil não permite esta operação."),
                };
            });
        services.AddAuthorization();
        return services;
    }

    /// <summary>Quem está na sessão, a partir das claims do token validado.</summary>
    public static UsuarioAutenticado ObterUsuario(this ClaimsPrincipal principal) => new(
        Guid.Parse(principal.FindFirstValue(ClaimId)!),
        principal.FindFirstValue(ClaimNome)!,
        principal.FindFirstValue(ClaimEmail)!,
        Enum.Parse<PerfilUsuario>(principal.FindFirstValue(ClaimPerfil)!));

    private static async Task EscreverProblemaAsync(HttpContext http, int status, string detalhe)
    {
        http.Response.StatusCode = status;
        var problemas = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemas.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = status, Detail = detalhe },
        });
    }
}
