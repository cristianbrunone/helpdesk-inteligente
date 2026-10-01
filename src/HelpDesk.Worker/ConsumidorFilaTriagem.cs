using System.Diagnostics;
using HelpDesk.Application.Triagem;

namespace HelpDesk.Worker;

/// <summary>
/// Consome a fila de triagem (ADR-0003, ADR-0010): reserva um lote, processa cada triagem num escopo próprio e,
/// com a fila vazia, espera o intervalo de polling. Com a triagem desativada (ADR-0021), não consome nada: as
/// pendentes ficam na fila até a flag voltar. Erros no lote (ex.: banco fora) não derrubam o Worker.
/// </summary>
public sealed partial class ConsumidorFilaTriagem(
    IServiceScopeFactory escopos,
    OpcoesIA opcoesIA,
    OpcoesFila opcoes,
    ILogger<ConsumidorFilaTriagem> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opcoesIA.TriagemHabilitada)
        {
            // A flag só muda reiniciando o contêiner (ADR-0021); não há o que consultar até lá.
            LogDesativada(logger);
            return;
        }

        LogIniciado(logger, opcoes.Lote, opcoes.IntervaloPolling.TotalMilliseconds, opcoes.Lease.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var processadas = 0;
            try
            {
                processadas = await ProcessarLoteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception erro)
            {
                LogErroNoLote(logger, erro.GetType().Name);
            }

            if (processadas == 0)
            {
                try
                {
                    await Task.Delay(opcoes.IntervaloPolling, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        LogEncerrado(logger);
    }

    /// <summary>Reserva e processa um lote. Público para os testes conduzirem a fila passo a passo.</summary>
    public async Task<int> ProcessarLoteAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> reservadas;
        await using (var escopo = escopos.CreateAsyncScope())
        {
            reservadas = await escopo.ServiceProvider.GetRequiredService<IFilaTriagem>()
                .ReservarAsync(opcoes.Lote, opcoes.Lease, cancellationToken);
        }

        foreach (var triagemId in reservadas)
        {
            // Um escopo (DbContext) por triagem: a falha de uma não contamina as outras.
            await using var escopo = escopos.CreateAsyncScope();
            var inicio = Stopwatch.GetTimestamp();
            var resultado = await escopo.ServiceProvider.GetRequiredService<ProcessarTriagemPendente>()
                .ExecutarAsync(triagemId, opcoes.MaxReservas, cancellationToken);
            if (resultado is not null)
            {
                LogProcessada(logger, triagemId, resultado.Status.ToString(), resultado.Codigo ?? "-",
                    Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
            }
        }

        return reservadas.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Triagem por IA desativada (IA_TRIAGEM_HABILITADA=false): a fila não será consumida")]
    private static partial void LogDesativada(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Consumidor da fila de triagem iniciado (lote {Lote}, polling {PollingMs} ms, lease {LeaseSegundos} s)")]
    private static partial void LogIniciado(ILogger logger, int lote, double pollingMs, double leaseSegundos);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Triagem {TriagemId} processada: {Status} ({Codigo}) em {DuracaoMs:0} ms")]
    private static partial void LogProcessada(
        ILogger logger, Guid triagemId, string status, string codigo, double duracaoMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro ao processar o lote da fila de triagem ({TipoErro})")]
    private static partial void LogErroNoLote(ILogger logger, string tipoErro);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumidor da fila de triagem encerrado")]
    private static partial void LogEncerrado(ILogger logger);
}
