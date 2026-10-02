using System.ClientModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Resiliência do cliente de chat (NFR-04, ADR-0005; decisão da Sprint 2: as novas tentativas ficam aqui, e não
/// na fila). Cada tentativa tem timeout próprio; falhas transitórias (429, 5xx, rede, timeout) são repetidas com
/// backoff exponencial + jitter, respeitando o <c>Retry-After</c>. Esgotadas as tentativas, lança
/// <see cref="ProvedorIndisponivelException"/>. Falhas definitivas (400, 401...) e o cancelamento externo sobem
/// na hora. Cada tentativa é um span <c>llm.tentativa</c>, sem conteúdo.
/// </summary>
public sealed partial class ResilienciaChatClient(
    IChatClient interno,
    OpcoesLlm opcoes,
    ILogger<ResilienciaChatClient> logger,
    Func<TimeSpan, CancellationToken, Task>? esperar = null,
    Random? aleatorio = null) : DelegatingChatClient(interno)
{
    public const string NomeFonteAtividades = "HelpDesk.IA";

    private static readonly ActivitySource _fonte = new(NomeFonteAtividades);
    private static readonly TimeSpan _esperaBase = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _esperaMaxima = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _retryAfterMaximo = TimeSpan.FromSeconds(60);

    private readonly Func<TimeSpan, CancellationToken, Task> _esperar = esperar ?? Task.Delay;
    private readonly Random _aleatorio = aleatorio ?? Random.Shared;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var mensagens = messages as IList<ChatMessage> ?? [.. messages];
        var totalTentativas = opcoes.MaxRetries + 1;

        for (var tentativa = 1; ; tentativa++)
        {
            using var atividade = _fonte.StartActivity("llm.tentativa");
            atividade?.SetTag("llm.tentativa", tentativa);
            try
            {
                var resposta = await TentarAsync(mensagens, options, cancellationToken);
                atividade?.SetTag("llm.resultado", "sucesso");
                return resposta;
            }
            catch (Exception erro) when (Transitoria(erro, cancellationToken) is { } falha)
            {
                atividade?.SetTag("llm.resultado", "transitoria");
                atividade?.SetTag("llm.erro_tipo", falha.Tipo);
                atividade?.SetStatus(ActivityStatusCode.Error, falha.Tipo);

                if (tentativa >= totalTentativas)
                {
                    LogEsgotou(logger, totalTentativas, falha.Tipo);
                    throw falha;
                }

                var espera = CalcularEspera(tentativa, falha.RetryAfter, _aleatorio);
                LogNovaTentativa(logger, tentativa, totalTentativas, falha.Tipo, espera.TotalMilliseconds);
                await _esperar(espera, cancellationToken);
            }
            catch (Exception erro) when (!cancellationToken.IsCancellationRequested)
            {
                atividade?.SetTag("llm.resultado", "erro");
                atividade?.SetStatus(ActivityStatusCode.Error, erro.GetType().Name);
                throw;
            }
        }
    }

    /// <summary>
    /// Streaming (copiloto, ADR-0012). Repete só <b>antes do primeiro pedaço</b>: depois que algo foi entregue,
    /// repetir duplicaria texto na tela, então a falha sobe já classificada. O prazo vale até o primeiro pedaço e,
    /// depois, entre um pedaço e o seguinte (inatividade), para um stream longo e saudável não ser cortado.
    /// </summary>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var mensagens = messages as IList<ChatMessage> ?? [.. messages];
        var totalTentativas = opcoes.MaxRetries + 1;

        for (var tentativa = 1; ; tentativa++)
        {
            using var atividade = _fonte.StartActivity("llm.tentativa");
            atividade?.SetTag("llm.tentativa", tentativa);
            atividade?.SetTag("llm.streaming", true);

            using var prazo = new CancellationTokenSource(opcoes.Timeout);
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, prazo.Token);
            var opcoesComPrazo = ContextoUsoLlm.ComPrazo(options, prazo.Token);
            await using var enumerador = base.GetStreamingResponseAsync(mensagens, opcoesComPrazo, limite.Token)
                .GetAsyncEnumerator(limite.Token);

            ProvedorIndisponivelException? falha;
            bool temPrimeiro;
            try
            {
                temPrimeiro = await enumerador.MoveNextAsync();
                falha = null;
            }
            catch (Exception erro) when (TransitoriaNoStream(erro, cancellationToken) is { } transitoria)
            {
                temPrimeiro = false;
                falha = transitoria;
            }
            catch (Exception erro) when (Marcar(atividade, erro, cancellationToken))
            {
                throw; // nunca chega aqui: o filtro só marca o span
            }

            if (falha is not null)
            {
                atividade?.SetTag("llm.resultado", "transitoria");
                atividade?.SetTag("llm.erro_tipo", falha.Tipo);
                atividade?.SetStatus(ActivityStatusCode.Error, falha.Tipo);
                if (tentativa >= totalTentativas)
                {
                    LogEsgotou(logger, totalTentativas, falha.Tipo);
                    throw falha;
                }

                var espera = CalcularEspera(tentativa, falha.RetryAfter, _aleatorio);
                LogNovaTentativa(logger, tentativa, totalTentativas, falha.Tipo, espera.TotalMilliseconds);
                await _esperar(espera, cancellationToken);
                continue;
            }

            if (temPrimeiro)
            {
                yield return enumerador.Current;
                while (true)
                {
                    prazo.CancelAfter(opcoes.Timeout);
                    bool temProximo;
                    try
                    {
                        temProximo = await enumerador.MoveNextAsync();
                    }
                    catch (Exception erro) when (TransitoriaNoStream(erro, cancellationToken) is { } noMeio)
                    {
                        atividade?.SetTag("llm.resultado", "falha_no_meio_do_stream");
                        atividade?.SetTag("llm.erro_tipo", noMeio.Tipo);
                        atividade?.SetStatus(ActivityStatusCode.Error, noMeio.Tipo);
                        throw noMeio;
                    }
                    catch (Exception erro) when (Marcar(atividade, erro, cancellationToken))
                    {
                        throw;
                    }

                    if (!temProximo)
                    {
                        break;
                    }

                    yield return enumerador.Current;
                }
            }

            atividade?.SetTag("llm.resultado", "sucesso");
            yield break;
        }
    }

    /// <summary>
    /// Backoff exponencial (1 s, 2 s, 4 s...) com jitter entre 50% e 100%, limitado a 30 s. Se o provedor pediu
    /// <c>Retry-After</c>, espera pelo menos isso (até 60 s).
    /// </summary>
    internal static TimeSpan CalcularEspera(int tentativa, TimeSpan? retryAfter, Random aleatorio)
    {
        var exponencial = _esperaBase * Math.Pow(2, tentativa - 1);
        var limitada = exponencial < _esperaMaxima ? exponencial : _esperaMaxima;
        var comJitter = limitada * (0.5 + (aleatorio.NextDouble() * 0.5));

        if (retryAfter is not { } pedido)
        {
            return comJitter;
        }

        var respeitado = pedido < _retryAfterMaximo ? pedido : _retryAfterMaximo;
        return respeitado > comJitter ? respeitado : comJitter;
    }

    /// <summary>A falha é transitória? Devolve-a já classificada, ou <c>null</c> se deve subir como está.</summary>
    internal static ProvedorIndisponivelException? Transitoria(Exception erro, CancellationToken externo) => erro switch
    {
        // Cancelamento de fora (o Worker está parando) não é falha do provedor.
        OperationCanceledException when externo.IsCancellationRequested => null,
        ProvedorIndisponivelException jaClassificada => jaClassificada,
        ClientResultException { Status: 429 } http => new(ProvedorIndisponivelException.TipoRateLimit,
            "O provedor recusou por limite de requisições (HTTP 429).", RetryAfterDe(http)),
        ClientResultException { Status: 500 or 502 or 503 or 504 } http => new(
            ProvedorIndisponivelException.TipoIndisponivel,
            $"O provedor está indisponível (HTTP {http.Status}).", RetryAfterDe(http)),
        // Sem resposta HTTP (status 0): o SDK embrulha falhas de transporte (conexão, DNS, TLS) nesta exceção.
        ClientResultException { Status: 0 } or HttpRequestException => new(
            ProvedorIndisponivelException.TipoIndisponivel, "Falha de rede ao chamar o provedor."),
        _ => null,
    };

    /// <summary>No stream, o prazo cancela o token: um cancelamento que não veio de fora é timeout.</summary>
    private ProvedorIndisponivelException? TransitoriaNoStream(Exception erro, CancellationToken externo) =>
        erro is OperationCanceledException && !externo.IsCancellationRequested
            ? new ProvedorIndisponivelException(ProvedorIndisponivelException.TipoTimeout,
                $"O provedor ficou {opcoes.Timeout.TotalSeconds:0} s sem enviar nada.")
            : Transitoria(erro, externo);

    /// <summary>Marca o span de uma falha definitiva e devolve <c>false</c>: usado num filtro, não captura nada.</summary>
    private static bool Marcar(Activity? atividade, Exception erro, CancellationToken externo)
    {
        if (!externo.IsCancellationRequested)
        {
            atividade?.SetTag("llm.resultado", "erro");
            atividade?.SetStatus(ActivityStatusCode.Error, erro.GetType().Name);
        }

        return false;
    }

    private async Task<ChatResponse> TentarAsync(
        IList<ChatMessage> mensagens, ChatOptions? options, CancellationToken externo)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(externo);
        limite.CancelAfter(opcoes.Timeout);
        try
        {
            return await base.GetResponseAsync(mensagens, options, limite.Token);
        }
        catch (OperationCanceledException) when (!externo.IsCancellationRequested)
        {
            throw new ProvedorIndisponivelException(ProvedorIndisponivelException.TipoTimeout,
                $"O provedor não respondeu em {opcoes.Timeout.TotalSeconds:0} s.");
        }
    }

    private static TimeSpan? RetryAfterDe(ClientResultException http)
    {
        if (http.GetRawResponse()?.Headers is not { } headers || !headers.TryGetValue("Retry-After", out var valor))
        {
            return null;
        }

        // O header pode vir em segundos ("12") ou como data HTTP.
        if (double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var segundos) && segundos >= 0)
        {
            return TimeSpan.FromSeconds(segundos);
        }

        return DateTimeOffset.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var quando)
            && quando > DateTimeOffset.UtcNow
            ? quando - DateTimeOffset.UtcNow
            : null;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Falha transitória do provedor de IA ({TipoErro}) na tentativa {Tentativa} de {Total}; nova tentativa em {EsperaMs:0} ms")]
    private static partial void LogNovaTentativa(
        ILogger logger, int tentativa, int total, string tipoErro, double esperaMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Provedor de IA indisponível após {Total} tentativas ({TipoErro})")]
    private static partial void LogEsgotou(ILogger logger, int total, string tipoErro);
}
