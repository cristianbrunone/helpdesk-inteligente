using HelpDesk.Infrastructure.Configuracao;
using HelpDesk.Infrastructure.Ia;

namespace HelpDesk.Worker;

/// <summary>
/// Configuração do consumo da fila (ADR-0003). O lease precisa ser maior que o pior caso de uma triagem (todas as
/// tentativas no timeout + esperas), senão outra instância poderia reservar a mesma triagem.
/// </summary>
public sealed record OpcoesFila(int Lote, TimeSpan IntervaloPolling, TimeSpan Lease, int MaxReservas)
{
    public const string WorkerBatchSize = "WORKER_BATCH_SIZE";
    public const string WorkerPollIntervalMs = "WORKER_POLL_INTERVAL_MS";
    public const string WorkerLeaseSeconds = "WORKER_LEASE_SECONDS";

    /// <summary>Quantas vezes a mesma triagem pode ser reservada antes de virar "interrompida".</summary>
    public const int MaxReservasPadrao = 3;

    public static OpcoesFila Ler(LeitorAmbiente leitor, OpcoesLlm llm)
    {
        // Padrão: tempo de todas as tentativas no timeout, mais um minuto de folga para as esperas entre elas.
        var piorCaso = (int)(llm.Timeout.TotalSeconds * (llm.MaxRetries + 1)) + 60;
        return new OpcoesFila(
            Lote: leitor.Inteiro(WorkerBatchSize, padrao: 5, minimo: 1, maximo: 100),
            IntervaloPolling: TimeSpan.FromMilliseconds(leitor.Inteiro(WorkerPollIntervalMs, 1500, 100, 60_000)),
            Lease: TimeSpan.FromSeconds(leitor.Inteiro(WorkerLeaseSeconds, Math.Max(piorCaso, 300), 10, 3600)),
            MaxReservas: MaxReservasPadrao);
    }
}
