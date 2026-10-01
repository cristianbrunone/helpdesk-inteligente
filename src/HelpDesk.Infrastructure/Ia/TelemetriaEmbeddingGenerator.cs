using System.Diagnostics;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Telemetria de toda geração de embeddings (RF-17, NFR-11), como a do chat: uma linha em <c>uso_llm</c> com a
/// operação <c>embedding</c> e um log estruturado por requisição ao provedor (um lote de textos). Fica por dentro
/// da resiliência, então cada tentativa é um registro. Nunca registra os textos nem os vetores.
/// </summary>
public sealed partial class TelemetriaEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> interno,
    OpcoesLlm opcoes,
    IRegistroUsoLlm registro,
    ILogger<TelemetriaEmbeddingGenerator> logger,
    TimeProvider? relogio = null) : DelegatingEmbeddingGenerator<string, Embedding<float>>(interno)
{
    private readonly TimeProvider _relogio = relogio ?? TimeProvider.System;

    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var textos = values as IList<string> ?? [.. values];
        var inicio = Stopwatch.GetTimestamp();
        try
        {
            var resultado = await base.GenerateAsync(textos, options, cancellationToken);
            await RegistrarAsync(textos.Count, inicio, resultado.Usage, erroTipo: null);
            return resultado;
        }
        catch (Exception erro)
        {
            await RegistrarAsync(textos.Count, inicio, uso: null, TelemetriaChatClient.TipoDoErro(erro));
            throw;
        }
    }

    private async Task RegistrarAsync(int textos, long inicio, UsageDetails? uso, string? erroTipo)
    {
        var latenciaMs = (int)Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
        var tokens = (int?)uso?.InputTokenCount;

        LogChamada(logger, erroTipo is null ? LogLevel.Information : LogLevel.Warning, textos, opcoes.NomeProvedor,
            opcoes.ModeloEmbeddingEfetivo, latenciaMs, tokens, erroTipo is null, erroTipo ?? "-");

        await registro.RegistrarAsync(new RegistroUsoLlm(RegistroUsoLlm.OperacaoEmbedding, null, null,
            opcoes.NomeProvedor, opcoes.ModeloEmbeddingEfetivo, tokens, null, latenciaMs, erroTipo is null, erroTipo,
            _relogio.GetUtcNow()));
    }

    [LoggerMessage(Message = "Embeddings: {Textos} textos via {Provedor}/{Modelo} em {LatenciaMs} ms, tokens " +
        "{TokensEntrada}, sucesso {Sucesso}, erro {ErroTipo}")]
    private static partial void LogChamada(ILogger logger, LogLevel nivel, int textos, string provedor,
        string modelo, int latenciaMs, int? tokensEntrada, bool sucesso, string erroTipo);
}
