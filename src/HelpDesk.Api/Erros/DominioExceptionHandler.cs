using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HelpDesk.Api.Erros;

/// <summary>
/// O único ponto que converte exceções em ProblemDetails (ADR-0013). Na Sprint 1 passa a mapear os erros tipados do
/// domínio (409/404); hoje cobre requisição malformada (400) e erro inesperado (500).
/// </summary>
internal sealed partial class DominioExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<DominioExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        var (status, detalhe) = exception switch
        {
            BadHttpRequestException bad => (bad.StatusCode, "A requisição é malformada."),
            _ => (StatusCodes.Status500InternalServerError,
                "Ocorreu um erro inesperado. Informe o correlationId ao suporte."),
        };

        // A mensagem e o stack trace vão só para o log; a resposta nunca os expõe.
        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogErroInesperado(logger, exception);
        }

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Detail = detalhe },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro inesperado ao processar a requisição")]
    private static partial void LogErroInesperado(ILogger logger, Exception exception);
}
