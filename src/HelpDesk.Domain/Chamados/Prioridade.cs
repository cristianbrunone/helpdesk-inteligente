namespace HelpDesk.Domain.Chamados;

/// <summary>
/// Enum nativo <c>prioridade_chamado</c> no PostgreSQL. A ordem de declaração é a ordem de negócio (P-07):
/// <c>ORDER BY prioridade</c> ordena Baixa &lt; Média &lt; Alta &lt; Crítica sem <c>CASE</c>.
/// </summary>
public enum Prioridade
{
    Baixa,
    Media,
    Alta,
    Critica,
}
