using HelpDesk.Domain.Chamados;

namespace HelpDesk.Application.Dashboard;

/// <summary>
/// Resumo do dashboard (RF-40 a RF-43; contrato: <c>GET /api/dashboard/resumo</c>). Tudo é agregado no banco, em SQL
/// explícito (ADR-0009): nenhum chamado individual chega à aplicação.
/// </summary>
public sealed record ResumoDashboard(
    int TotalChamados,
    IReadOnlyList<TotalPorStatus> PorStatus,
    IReadOnlyList<TotalPorPrioridade> PorPrioridade,
    IReadOnlyList<TempoMedioPorCategoria> TempoMedioResolucaoPorCategoria,
    ResumoIA Ia);

/// <summary>Todos os status aparecem, na ordem de negócio, mesmo com total zero (o gráfico não "pula" barras).</summary>
public sealed record TotalPorStatus(StatusChamado Status, int Total);

public sealed record TotalPorPrioridade(Prioridade Prioridade, int Total);

/// <summary>RN-13: só Resolvido/Fechado. <see cref="TempoMedioHoras"/> é nulo para categoria sem resolvidos.</summary>
public sealed record TempoMedioPorCategoria(short CategoriaId, string Categoria, int Resolvidos, decimal? TempoMedioHoras);

/// <summary>
/// Qualidade e custo da IA. Aceitas e rejeitadas contam todas as decisões (inclusive de triagens refeitas depois),
/// e a taxa é nula enquanto não houver decisão (RN-12). Pendentes é a fila agora; falhas, todas as que falharam.
/// </summary>
public sealed record ResumoIA(
    decimal? TaxaAceitacao,
    int Aceitas,
    int Rejeitadas,
    int Pendentes,
    int Falhas,
    IReadOnlyList<AceitacaoPorCategoria> PorCategoria,
    IReadOnlyList<ConsumoIA> Consumo30d);

public sealed record AceitacaoPorCategoria(string Categoria, int Aceitas, int Rejeitadas, decimal? TaxaAceitacao);

/// <summary>Livro-razão <c>uso_llm</c> dos últimos 30 dias, por operação e modelo (cada tentativa conta).</summary>
public sealed record ConsumoIA(
    string Operacao,
    string Modelo,
    int Chamadas,
    int Falhas,
    long? TokensEntrada,
    long? TokensSaida,
    double? LatenciaP95Ms);

/// <summary>Porta das consultas analíticas do dashboard (implementadas em SQL explícito na Infrastructure).</summary>
public interface IConsultaDashboard
{
    Task<ResumoDashboard> ObterResumoAsync(CancellationToken cancellationToken);
}

/// <summary>O caso de uso do dashboard: só leitura, sem regra além da consulta.</summary>
public sealed class ObterResumoDashboard(IConsultaDashboard consulta)
{
    public Task<ResumoDashboard> ExecutarAsync(CancellationToken cancellationToken) =>
        consulta.ObterResumoAsync(cancellationToken);
}
