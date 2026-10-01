namespace HelpDesk.Domain.Chamados;

/// <summary>
/// Registro de uma mudança de status (RN-02). A criação gera <c>null → Aberto</c> com autor "sistema" (P-10).
/// </summary>
public sealed class HistoricoStatus
{
    public const int AlteradoPorTamanhoMaximo = 120;

    /// <summary>Gerado pelo banco (<c>bigint identity</c>).</summary>
    public long Id { get; private set; }

    public Guid ChamadoId { get; private set; }

    public StatusChamado? StatusAnterior { get; private set; }

    public StatusChamado StatusNovo { get; private set; }

    public DateTimeOffset AlteradoEm { get; private set; }

    public string AlteradoPor { get; private set; }

    internal HistoricoStatus(
        Guid chamadoId,
        StatusChamado? statusAnterior,
        StatusChamado statusNovo,
        DateTimeOffset alteradoEm,
        string alteradoPor)
    {
        ChamadoId = chamadoId;
        StatusAnterior = statusAnterior;
        StatusNovo = statusNovo;
        AlteradoEm = alteradoEm;
        AlteradoPor = alteradoPor;
    }
}
