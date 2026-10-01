namespace HelpDesk.Worker;

/// <summary>
/// Batimento periódico no log, para provar que o Worker está vivo (Sprint 0). Na Sprint 2 os BackgroundServices da
/// fila de triagem e do reconciliador passam a conviver com este.
/// </summary>
public sealed partial class Heartbeat(ILogger<Heartbeat> logger, TimeSpan intervalo) : BackgroundService
{
    public const string VariavelIntervalo = "WORKER_HEARTBEAT_INTERVAL_SECONDS";
    public static readonly TimeSpan IntervaloPadrao = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogIniciado(logger, intervalo.TotalSeconds);

        using var timer = new PeriodicTimer(intervalo);
        long batimentos = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                LogBatimento(logger, ++batimentos);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento normal do host (Ctrl+C / docker stop).
        }

        LogEncerrado(logger, batimentos);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker iniciado; heartbeat a cada {IntervaloSegundos} s")]
    private static partial void LogIniciado(ILogger logger, double intervaloSegundos);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker ativo (heartbeat {Batimento})")]
    private static partial void LogBatimento(ILogger logger, long batimento);

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker encerrado após {Batimentos} heartbeats")]
    private static partial void LogEncerrado(ILogger logger, long batimentos);
}
