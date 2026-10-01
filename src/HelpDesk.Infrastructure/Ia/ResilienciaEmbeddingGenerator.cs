using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Resiliência da geração de embeddings, com as mesmas regras do chat (<see cref="ResilienciaChatClient"/>): timeout
/// por tentativa, novas tentativas para 429/5xx/rede/timeout com backoff exponencial + jitter e <c>Retry-After</c>,
/// e <see cref="ProvedorIndisponivelException"/> ao esgotar. O free tier do Gemini tem 100 requisições por minuto,
/// e o reconciliador manda lotes seguidos: o 429 é esperado na primeira indexação.
/// </summary>
public sealed partial class ResilienciaEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> interno,
    OpcoesLlm opcoes,
    ILogger<ResilienciaEmbeddingGenerator> logger,
    Func<TimeSpan, CancellationToken, Task>? esperar = null,
    Random? aleatorio = null) : DelegatingEmbeddingGenerator<string, Embedding<float>>(interno)
{
    private static readonly ActivitySource _fonte = new(ResilienciaChatClient.NomeFonteAtividades);

    private readonly Func<TimeSpan, CancellationToken, Task> _esperar = esperar ?? Task.Delay;
    private readonly Random _aleatorio = aleatorio ?? Random.Shared;

    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var textos = values as IList<string> ?? [.. values];
        var totalTentativas = opcoes.MaxRetries + 1;

        for (var tentativa = 1; ; tentativa++)
        {
            using var atividade = _fonte.StartActivity("embedding.tentativa");
            atividade?.SetTag("llm.tentativa", tentativa);
            atividade?.SetTag("embedding.textos", textos.Count);
            try
            {
                var resultado = await TentarAsync(textos, options, cancellationToken);
                atividade?.SetTag("llm.resultado", "sucesso");
                return resultado;
            }
            catch (Exception erro) when (ResilienciaChatClient.Transitoria(erro, cancellationToken) is { } falha)
            {
                atividade?.SetTag("llm.resultado", "transitoria");
                atividade?.SetTag("llm.erro_tipo", falha.Tipo);
                atividade?.SetStatus(ActivityStatusCode.Error, falha.Tipo);

                if (tentativa >= totalTentativas)
                {
                    LogEsgotou(logger, totalTentativas, falha.Tipo);
                    throw falha;
                }

                var espera = ResilienciaChatClient.CalcularEspera(tentativa, falha.RetryAfter, _aleatorio);
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

    private async Task<GeneratedEmbeddings<Embedding<float>>> TentarAsync(
        IList<string> textos, EmbeddingGenerationOptions? options, CancellationToken externo)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(externo);
        limite.CancelAfter(opcoes.Timeout);
        try
        {
            return await base.GenerateAsync(textos, options, limite.Token);
        }
        catch (OperationCanceledException) when (!externo.IsCancellationRequested)
        {
            throw new ProvedorIndisponivelException(ProvedorIndisponivelException.TipoTimeout,
                $"O provedor de embeddings não respondeu em {opcoes.Timeout.TotalSeconds:0} s.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Falha transitória do provedor de embeddings ({TipoErro}) na tentativa {Tentativa} de {Total}; nova tentativa em {EsperaMs:0} ms")]
    private static partial void LogNovaTentativa(
        ILogger logger, int tentativa, int total, string tipoErro, double esperaMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Provedor de embeddings indisponível após {Total} tentativas ({TipoErro})")]
    private static partial void LogEsgotou(ILogger logger, int total, string tipoErro);
}
