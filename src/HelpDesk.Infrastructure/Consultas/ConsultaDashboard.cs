using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using HelpDesk.Application.Dashboard;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Consultas;

/// <summary>
/// O dashboard em SQL explícito (ADR-0009): cada consulta é um arquivo versionado em <c>Consultas/Sql</c>, executado
/// com <c>Database.SqlQuery</c>. As cinco rodam na mesma conexão, numa transação <c>REPEATABLE READ</c> somente
/// leitura: todas veem o mesmo instantâneo do banco, então os totais batem entre si mesmo com escritas no meio.
/// </summary>
internal sealed class ConsultaDashboard(HelpDeskDbContext db) : IConsultaDashboard
{
    public async Task<ResumoDashboard> ObterResumoAsync(CancellationToken cancellationToken)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);

        var totais = await ConsultarAsync<LinhaTotal>("totais_por_status_e_prioridade", cancellationToken);
        var tempos = await ConsultarAsync<LinhaTempoMedio>("tempo_medio_por_categoria", cancellationToken);
        var aceitacao = await ConsultarAsync<LinhaAceitacao>("aceitacao_por_categoria", cancellationToken);
        var situacao = (await ConsultarAsync<LinhaSituacao>("situacao_triagens", cancellationToken)).Single();
        var consumo = await ConsultarAsync<LinhaConsumo>("consumo_ia_30_dias", cancellationToken);

        await transacao.CommitAsync(cancellationToken);

        // A linha de total geral do ROLLUP tem categoria nula; sem decisões, ela não existe.
        var geral = aceitacao.SingleOrDefault(l => l.Categoria is null);
        return new ResumoDashboard(
            TotalChamados: totais.Where(t => t.Dimensao == "status").Sum(t => t.Total),
            PorStatus: [.. Enum.GetValues<StatusChamado>().Select(s => new TotalPorStatus(s, Total(totais, "status", s)))],
            PorPrioridade:
            [
                .. Enum.GetValues<Prioridade>().Select(p => new TotalPorPrioridade(p, Total(totais, "prioridade", p))),
            ],
            TempoMedioResolucaoPorCategoria:
            [
                .. tempos.Select(t => new TempoMedioPorCategoria(t.CategoriaId, t.Categoria, t.Resolvidos, t.TempoMedioHoras)),
            ],
            Ia: new ResumoIA(
                geral?.TaxaAceitacao,
                geral?.Aceitas ?? 0,
                geral?.Rejeitadas ?? 0,
                situacao.Pendentes,
                situacao.Falhas,
                [
                    .. aceitacao.Where(l => l.Categoria is not null)
                        .Select(l => new AceitacaoPorCategoria(l.Categoria!, l.Aceitas, l.Rejeitadas, l.TaxaAceitacao)),
                ],
                [
                    .. consumo.Select(c => new ConsumoIA(c.Operacao, c.Modelo, c.Chamadas, c.Falhas, c.TokensEntrada,
                        c.TokensSaida, c.LatenciaP95Ms)),
                ]));
    }

    /// <summary>O total de um valor do enum, comparando com o rótulo nativo do PostgreSQL (snake_case).</summary>
    private static int Total<TEnum>(IReadOnlyList<LinhaTotal> totais, string dimensao, TEnum valor)
        where TEnum : struct, Enum
    {
        var rotulo = NomesSnakeCase.Converter(valor.ToString());
        return totais.SingleOrDefault(t => t.Dimensao == dimensao && t.Valor == rotulo)?.Total ?? 0;
    }

    private async Task<IReadOnlyList<T>> ConsultarAsync<T>(string arquivo, CancellationToken cancellationToken) =>
        await db.Database.SqlQueryRaw<T>(ArquivosSql.Ler(arquivo)).ToListAsync(cancellationToken);

    // Linhas exatamente como o SQL devolve (colunas em snake_case); o mapeamento para o contrato é manual (ADR-0002).
    private sealed record LinhaTotal(
        [property: Column("dimensao")] string Dimensao,
        [property: Column("valor")] string Valor,
        [property: Column("total")] int Total);

    private sealed record LinhaTempoMedio(
        [property: Column("categoria_id")] short CategoriaId,
        [property: Column("categoria")] string Categoria,
        [property: Column("resolvidos")] int Resolvidos,
        [property: Column("tempo_medio_horas")] decimal? TempoMedioHoras);

    private sealed record LinhaAceitacao(
        [property: Column("categoria")] string? Categoria,
        [property: Column("aceitas")] int Aceitas,
        [property: Column("rejeitadas")] int Rejeitadas,
        [property: Column("taxa_aceitacao")] decimal? TaxaAceitacao);

    private sealed record LinhaSituacao(
        [property: Column("pendentes")] int Pendentes,
        [property: Column("falhas")] int Falhas);

    private sealed record LinhaConsumo(
        [property: Column("operacao")] string Operacao,
        [property: Column("modelo")] string Modelo,
        [property: Column("chamadas")] int Chamadas,
        [property: Column("falhas")] int Falhas,
        [property: Column("tokens_entrada")] long? TokensEntrada,
        [property: Column("tokens_saida")] long? TokensSaida,
        [property: Column("latencia_p95_ms")] double? LatenciaP95Ms);
}

/// <summary>
/// As consultas analíticas em <c>Consultas/Sql/*.sql</c>, embutidas no assembly: o SQL fica legível e revisável
/// como arquivo, e nunca falta na imagem Docker. Cada uma tem teste de integração com dados controlados.
/// </summary>
internal static class ArquivosSql
{
    public static string Ler(string nome)
    {
        var recurso = $"HelpDesk.Infrastructure.Consultas.Sql.{nome}.sql";
        using var fluxo = typeof(ArquivosSql).Assembly.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"A consulta {recurso} não está embutida no assembly.");
        using var leitor = new StreamReader(fluxo);
        // O EF pode envolver a consulta; o ponto e vírgula final do arquivo não pode ir junto.
        return leitor.ReadToEnd().TrimEnd().TrimEnd(';');
    }
}
