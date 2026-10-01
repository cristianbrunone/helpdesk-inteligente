using System.ClientModel;
using System.Diagnostics;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Telemetria de toda chamada ao LLM (ADR-0005, RF-17, NFR-11), num único lugar para triagem e copiloto: grava
/// em <c>uso_llm</c> e escreve um log estruturado com provedor, modelo, latência, tokens, sucesso e tipo de erro.
/// Fica por dentro da resiliência, então cada tentativa é um registro. Nunca registra o prompt nem a resposta.
/// </summary>
public sealed partial class TelemetriaChatClient(
    IChatClient interno,
    OpcoesLlm opcoes,
    IRegistroUsoLlm registro,
    ILogger<TelemetriaChatClient> logger,
    TimeProvider? relogio = null) : DelegatingChatClient(interno)
{
    private readonly TimeProvider _relogio = relogio ?? TimeProvider.System;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inicio = Stopwatch.GetTimestamp();
        try
        {
            var resposta = await base.GetResponseAsync(messages, options, cancellationToken);
            await RegistrarAsync(options, inicio, resposta.ModelId, resposta.Usage, erroTipo: null);
            return resposta;
        }
        catch (Exception erro)
        {
            await RegistrarAsync(options, inicio, modelo: null, uso: null, TipoDoErro(erro));
            throw;
        }
    }

    /// <summary>Tipo da falha para <c>uso_llm.erro_tipo</c>: categoria, não mensagem (a mensagem pode ter conteúdo).</summary>
    internal static string TipoDoErro(Exception erro) => erro switch
    {
        ProvedorIndisponivelException indisponivel => indisponivel.Tipo,
        // O tempo de cada tentativa é cortado pela resiliência (token cancelado aqui dentro).
        OperationCanceledException => ProvedorIndisponivelException.TipoTimeout,
        ClientResultException { Status: 401 or 403 } => "autenticacao",
        ClientResultException { Status: 429 } => ProvedorIndisponivelException.TipoRateLimit,
        ClientResultException { Status: >= 500 } => ProvedorIndisponivelException.TipoIndisponivel,
        ClientResultException { Status: >= 400 } => "requisicao_recusada",
        HttpRequestException => ProvedorIndisponivelException.TipoIndisponivel,
        _ => "erro",
    };

    private async Task RegistrarAsync(
        ChatOptions? options, long inicio, string? modelo, UsageDetails? uso, string? erroTipo)
    {
        var latenciaMs = (int)Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
        var (operacao, triagemId, chamadoId) = ContextoUsoLlm.Ler(options);
        var modeloUsado = modelo ?? opcoes.ModeloEfetivo;
        var tokensEntrada = (int?)uso?.InputTokenCount;
        var tokensSaida = (int?)uso?.OutputTokenCount;

        LogChamada(logger, erroTipo is null ? LogLevel.Information : LogLevel.Warning, operacao ?? "(sem operação)",
            opcoes.NomeProvedor, modeloUsado, latenciaMs, tokensEntrada, tokensSaida, erroTipo is null,
            erroTipo ?? "-", triagemId, chamadoId);

        // Sem operação conhecida não há como classificar o custo; o log acima continua existindo.
        if (operacao is null)
        {
            return;
        }

        await registro.RegistrarAsync(new RegistroUsoLlm(operacao, triagemId, chamadoId, opcoes.NomeProvedor,
            modeloUsado, tokensEntrada, tokensSaida, latenciaMs, erroTipo is null, erroTipo, _relogio.GetUtcNow()));
    }

    [LoggerMessage(Message = "Chamada ao LLM: {Operacao} via {Provedor}/{Modelo} em {LatenciaMs} ms, tokens " +
        "{TokensEntrada}/{TokensSaida}, sucesso {Sucesso}, erro {ErroTipo} (triagem {TriagemId}, chamado {ChamadoId})")]
    private static partial void LogChamada(ILogger logger, LogLevel nivel, string operacao, string provedor,
        string modelo, int latenciaMs, int? tokensEntrada, int? tokensSaida, bool sucesso, string erroTipo,
        Guid? triagemId, Guid? chamadoId);
}
