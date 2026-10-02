using HelpDesk.Api.Observabilidade;

namespace HelpDesk.Api.Erros;

/// <summary>Formato único de erro (contrato §2): <c>codigo</c>, <c>type</c>, <c>instance</c> e <c>correlationId</c>.</summary>
internal static class ConfiguracaoProblemDetails
{
    private const string TipoBase = "https://helpdesk.local/problemas/";

    // Padrões por status HTTP. Erros específicos (ex.: 409 transicao_invalida) definem o próprio código.
    private static readonly Dictionary<int, (string Codigo, string Titulo)> _padroes = new()
    {
        [StatusCodes.Status400BadRequest] = ("requisicao_invalida", "Requisição inválida"),
        [StatusCodes.Status401Unauthorized] = ("nao_autenticado", "Não autenticado"),
        [StatusCodes.Status403Forbidden] = ("acesso_negado", "Acesso negado"),
        [StatusCodes.Status404NotFound] = ("nao_encontrado", "Recurso não encontrado"),
        [StatusCodes.Status405MethodNotAllowed] = ("requisicao_invalida", "Método não permitido"),
        [StatusCodes.Status500InternalServerError] = ("erro_interno", "Erro interno"),
    };

    public static IServiceCollection AdicionarProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = contexto =>
        {
            var problema = contexto.ProblemDetails;
            var http = contexto.HttpContext;

            problema.Instance ??= http.Request.Path;
            problema.Extensions["correlationId"] = CorrelacaoMiddleware.Obter(http);

            if (!problema.Extensions.ContainsKey("codigo") && _padroes.TryGetValue(problema.Status ?? 0, out var padrao))
            {
                problema.Extensions["codigo"] = padrao.Codigo;
                problema.Type = TipoBase + padrao.Codigo.Replace('_', '-');
                problema.Title = padrao.Titulo;
            }
        });
        services.AddExceptionHandler<DominioExceptionHandler>();
        return services;
    }
}
