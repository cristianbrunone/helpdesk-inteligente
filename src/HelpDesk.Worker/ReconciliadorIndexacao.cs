using System.Diagnostics;
using HelpDesk.Application.Conhecimento;
using HelpDesk.Infrastructure.Configuracao;

namespace HelpDesk.Worker;

/// <summary>Intervalo entre passadas do reconciliador (ADR-0010) e o tamanho do lote de cada etapa.</summary>
public sealed record OpcoesReconciliacao(TimeSpan Intervalo, int Lote)
{
    public const string WorkerReconcileIntervalSeconds = "WORKER_RECONCILE_INTERVAL_SECONDS";

    /// <summary>Textos por requisição de embeddings: poucas requisições (o free tier limita por minuto), sem lote gigante.</summary>
    public const int LotePadrao = 32;

    public static OpcoesReconciliacao Ler(LeitorAmbiente leitor) => new(
        TimeSpan.FromSeconds(leitor.Inteiro(WorkerReconcileIntervalSeconds, padrao: 30, minimo: 1, maximo: 3600)),
        LotePadrao);
}

/// <summary>
/// Mantém o índice do RAG em dia (ADR-0010): uma passada logo na subida e, enquanto houver trabalho (lote cheio),
/// a próxima em seguida; depois, uma a cada intervalo. Roda mesmo com a triagem desativada: indexar não gera
/// sugestão, e o índice precisa estar pronto quando a flag voltar. Erros (banco ou provedor fora) não derrubam o
/// Worker: o que ficou pendente é retomado na passada seguinte.
/// </summary>
public sealed partial class ReconciliadorIndexacao(
    IServiceScopeFactory escopos,
    OpcoesReconciliacao opcoes,
    ILogger<ReconciliadorIndexacao> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogIniciado(logger, opcoes.Intervalo.TotalSeconds, opcoes.Lote);
        while (!stoppingToken.IsCancellationRequested)
        {
            var haMais = false;
            try
            {
                var resultado = await ExecutarPassadaAsync(stoppingToken);
                haMais = resultado.Indexados == opcoes.Lote || resultado.Sincronizados >= opcoes.Lote;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception erro)
            {
                LogErro(logger, erro.GetType().Name);
            }

            if (!haMais)
            {
                try
                {
                    await Task.Delay(opcoes.Intervalo, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        LogEncerrado(logger);
    }

    /// <summary>Uma passada completa num escopo próprio. Público para os testes conduzirem o reconciliador.</summary>
    public async Task<ResultadoReconciliacao> ExecutarPassadaAsync(CancellationToken cancellationToken)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var inicio = Stopwatch.GetTimestamp();
        var resultado = await escopo.ServiceProvider.GetRequiredService<ReconciliarIndiceRag>()
            .ExecutarAsync(opcoes.Lote, cancellationToken);

        if (resultado is not { Removidos: 0, Sincronizados: 0, Indexados: 0 })
        {
            LogPassada(logger, resultado.Removidos, resultado.Sincronizados, resultado.Indexados,
                Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
        }

        return resultado;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Reconciliador de indexação iniciado (intervalo {IntervaloSegundos} s, lote {Lote})")]
    private static partial void LogIniciado(ILogger logger, double intervaloSegundos, int lote);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Índice do RAG: {Removidos} removidos, {Sincronizados} sincronizados, {Indexados} indexados em {DuracaoMs:0} ms")]
    private static partial void LogPassada(
        ILogger logger, int removidos, int sincronizados, int indexados, double duracaoMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Erro na passada do reconciliador de indexação ({TipoErro}); nova tentativa no próximo intervalo")]
    private static partial void LogErro(ILogger logger, string tipoErro);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconciliador de indexação encerrado")]
    private static partial void LogEncerrado(ILogger logger);
}
