namespace HelpDesk.Domain.Chamados;

/// <summary>Enum nativo <c>status_chamado</c> no PostgreSQL.</summary>
public enum StatusChamado
{
    Aberto,
    EmAndamento,
    Resolvido,
    Fechado,
    Cancelado,
}
