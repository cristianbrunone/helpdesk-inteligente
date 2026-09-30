namespace HelpDesk.Domain.Triagem;

/// <summary>Enum nativo <c>status_triagem</c> no PostgreSQL.</summary>
public enum StatusTriagem
{
    Pendente,
    Concluida,
    Falhou,
    Aceita,
    Rejeitada,
}
