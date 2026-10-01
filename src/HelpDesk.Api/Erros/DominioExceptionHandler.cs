using System.Text.Json;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Erros;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HelpDesk.Api.Erros;

/// <summary>
/// O único ponto que converte exceções em ProblemDetails (ADR-0013), seguindo o catálogo do contrato §2: erros de
/// domínio (404/409/422), parâmetro inválido e requisição malformada (400) e erro inesperado (500).
/// </summary>
internal sealed partial class DominioExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<DominioExceptionHandler> logger) : IExceptionHandler
{
    private const string TipoBase = "https://helpdesk.local/problemas/";

    // Status e título de cada código de erro de domínio (contrato §2).
    private static readonly Dictionary<string, (int Status, string Titulo)> _errosDominio = new()
    {
        ["requisicao_invalida"] = (StatusCodes.Status400BadRequest, "Requisição inválida"),
        ["nao_encontrado"] = (StatusCodes.Status404NotFound, "Recurso não encontrado"),
        ["validacao"] = (StatusCodes.Status422UnprocessableEntity, "Dados inválidos"),
        ["transicao_invalida"] = (StatusCodes.Status409Conflict, "Transição de status não permitida"),
        ["chamado_finalizado"] = (StatusCodes.Status409Conflict, "Chamado finalizado"),
        ["critico_nao_cancelavel"] = (StatusCodes.Status409Conflict, "Chamado crítico não pode ser cancelado"),
    };

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        var problema = exception switch
        {
            DominioException erro => ProblemaDeDominio(erro),
            BadHttpRequestException bad => new ProblemDetails { Status = bad.StatusCode, Detail = "A requisição é malformada." },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Detail = "Ocorreu um erro inesperado. Informe o correlationId ao suporte.",
            },
        };

        // A mensagem e o stack trace vão só para o log; a resposta nunca os expõe.
        if (problema.Status >= StatusCodes.Status500InternalServerError)
        {
            LogErroInesperado(logger, exception);
        }

        http.Response.StatusCode = problema.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = problema,
        });
    }

    private static ProblemDetails ProblemaDeDominio(DominioException erro)
    {
        var (status, titulo) = _errosDominio.TryGetValue(erro.Codigo, out var mapeado)
            ? mapeado
            : (StatusCodes.Status409Conflict, "Conflito com o estado atual");

        // A mensagem dos erros de domínio é escrita para o usuário (pt-BR, sem dados pessoais).
        ProblemDetails problema = erro is ValidacaoException validacao
            ? new HttpValidationProblemDetails(validacao.Erros.ToDictionary(
                e => JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value))
            : new ProblemDetails();

        problema.Status = status;
        problema.Title = titulo;
        problema.Detail = erro.Message;
        problema.Type = TipoBase + erro.Codigo.Replace('_', '-');
        problema.Extensions["codigo"] = erro.Codigo;

        if (erro is TransicaoInvalidaException transicao)
        {
            problema.Extensions["transicoesPermitidas"] = transicao.TransicoesPermitidas.Select(s => s.ToString()).ToArray();
        }

        return problema;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro inesperado ao processar a requisição")]
    private static partial void LogErroInesperado(ILogger logger, Exception exception);
}
