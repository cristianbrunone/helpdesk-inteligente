using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using HelpDesk.Api.Autenticacao;
using HelpDesk.Application;
using HelpDesk.Application.Copiloto;
using HelpDesk.Domain.Erros;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HelpDesk.Api.Endpoints;

/// <summary>Corpo da requisição ao copiloto (P-08, contrato §copiloto): histórico mantido pelo cliente.</summary>
public sealed record PerguntaCopiloto(IReadOnlyList<MensagemCopiloto>? Mensagens);

/// <summary>
/// Copiloto conversacional sobre SSE (ADR-0012, contrato §copiloto): emite eventos tipados de ferramenta, deltas
/// parciais, fontes verificadas, avisos e término, com rate limiter e guardrails.
/// </summary>
internal static class CopilotoEndpoints
{
    public const string NomePoliticaRateLimit = "copiloto";

    public static IEndpointRouteBuilder MapCopiloto(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chamados/{id:guid}/copiloto", Conversar)
            .RequireAuthorization(ConfiguracaoAutenticacao.PoliticaAtendente)
            .RequireRateLimiting(NomePoliticaRateLimit)
            .WithName("ConversarComCopiloto")
            .WithSummary("Conversa com o copiloto sobre o chamado em contexto via SSE (Server-Sent Events).")
            .WithTags("Copiloto")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> Conversar(
        Guid id,
        PerguntaCopiloto corpo,
        ConversarComCopiloto casoDeUso,
        CancellationToken cancellationToken)
    {
        // 1. Preparação (kill switch, validação das mensagens, chamado e prompt). Lança antes do stream iniciar (404/422/503).
        var conversa = await casoDeUso.PrepararAsync(id, corpo.Mensagens, cancellationToken);

        // 2. Transmissão contínua de eventos SSE
        return TypedResults.ServerSentEvents(GerarEventosAsync(casoDeUso, conversa, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<object>> GerarEventosAsync(
        ConversarComCopiloto casoDeUso,
        ConversaCopiloto conversa,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerador = casoDeUso.ResponderAsync(conversa, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            EventoCopiloto? evento = null;
            Exception? erroCapturado = null;
            try
            {
                if (!await enumerador.MoveNextAsync())
                {
                    break;
                }

                evento = enumerador.Current;
            }
            catch (Exception erro) when (erro is not OperationCanceledException)
            {
                erroCapturado = erro;
            }

            if (erroCapturado is not null)
            {
                yield return new SseItem<object>(CriarProblema(erroCapturado), "erro");
                yield break;
            }

            yield return Mapear(evento!);
        }
    }

    private static SseItem<object> Mapear(EventoCopiloto evento) => evento switch
    {
        EventoFerramenta f when f.Fase == EventoFerramenta.FaseIniciada =>
            new SseItem<object>(new { nome = f.Nome, fase = f.Fase, descricao = f.Descricao }, "ferramenta"),

        EventoFerramenta f =>
            new SseItem<object>(new { nome = f.Nome, fase = f.Fase, resultados = f.Resultados }, "ferramenta"),

        EventoDelta d =>
            new SseItem<object>(new { texto = d.Texto }, "delta"),

        EventoFontes fo =>
            new SseItem<object>(new { itens = fo.Itens }, "fontes"),

        EventoAviso a when a.Referencias is { Count: > 0 } =>
            new SseItem<object>(new { tipo = a.Tipo, referencias = a.Referencias }, "aviso"),

        EventoAviso a =>
            new SseItem<object>(new { tipo = a.Tipo }, "aviso"),

        EventoFim fi =>
            new SseItem<object>(new { tokensEntrada = fi.TokensEntrada, tokensSaida = fi.TokensSaida }, "fim"),

        _ => throw new InvalidOperationException($"Evento desconhecido: {evento.GetType().Name}")
    };

    private static ProblemDetails CriarProblema(Exception erro)
    {
        if (erro is DominioException dominio)
        {
            return new ProblemDetails
            {
                Status = dominio switch
                {
                    IaIndisponivelException => StatusCodes.Status503ServiceUnavailable,
                    RecursoNaoEncontradoException => StatusCodes.Status404NotFound,
                    ValidacaoException => StatusCodes.Status422UnprocessableEntity,
                    _ => StatusCodes.Status409Conflict,
                },
                Title = dominio is IaIndisponivelException ? "IA indisponível" : "Erro de domínio",
                Detail = dominio.Message,
                Type = $"https://helpdesk.local/problemas/{dominio.Codigo.Replace('_', '-')}",
                Extensions = { ["codigo"] = dominio.Codigo },
            };
        }

        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Erro inesperado",
            Detail = "Ocorreu um erro inesperado ao processar a resposta do copiloto.",
            Type = "https://helpdesk.local/problemas/erro-inesperado",
            Extensions = { ["codigo"] = "erro_inesperado" },
        };
    }
}
