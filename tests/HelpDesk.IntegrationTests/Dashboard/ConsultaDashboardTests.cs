using HelpDesk.Application.Dashboard;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Dashboard;

/// <summary>
/// Cada consulta do dashboard (ADR-0009) contra uma massa controlada, num banco só com as migrations (sem o seed):
/// os números esperados foram calculados à mão a partir dos dados inseridos abaixo.
/// </summary>
public sealed class ConsultaDashboardTests(BancoFixture banco)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resumo_MassaControlada_TotaisPorStatusEPrioridadeCompletosNaOrdemDeNegocio()
    {
        var resumo = await ResumoAsync(await BancoComMassaAsync());

        resumo.TotalChamados.ShouldBe(6);
        resumo.PorStatus.ShouldBe(
        [
            new(StatusChamado.Aberto, 1), new(StatusChamado.EmAndamento, 1), new(StatusChamado.Resolvido, 2),
            new(StatusChamado.Fechado, 1), new(StatusChamado.Cancelado, 1),
        ]);
        resumo.PorPrioridade.ShouldBe(
        [
            new(Prioridade.Baixa, 2), new(Prioridade.Media, 2), new(Prioridade.Alta, 1), new(Prioridade.Critica, 1),
        ]);
    }

    [Fact]
    public async Task Resumo_MassaControlada_TempoMedioSoComResolvidosEFechadosPorCategoria()
    {
        var resumo = await ResumoAsync(await BancoComMassaAsync());

        // Alfa: (2 h + 4 h) / 2; Beta: o fechado em 10 h (RN-13: Fechado conta); Gama: sem resolvidos → nulo.
        // O cancelado de Alfa e os abertos não entram.
        resumo.TempoMedioResolucaoPorCategoria.Select(t => (t.Categoria, t.Resolvidos, t.TempoMedioHoras)).ShouldBe(
        [
            ("Alfa", 2, 3.0m), ("Beta", 1, 10.0m), ("Gama", 0, (decimal?)null),
        ]);
    }

    [Fact]
    public async Task Resumo_MassaControlada_AceitacaoPorCategoriaComTotalGeralDoRollup()
    {
        var ia = (await ResumoAsync(await BancoComMassaAsync())).Ia;

        ia.PorCategoria.ShouldBe(
        [
            new("Alfa", Aceitas: 2, Rejeitadas: 1, TaxaAceitacao: 0.667m),
            new("Beta", Aceitas: 1, Rejeitadas: 0, TaxaAceitacao: 1.000m),
        ]);
        (ia.Aceitas, ia.Rejeitadas, ia.TaxaAceitacao).ShouldBe((3, 1, 0.750m));
        // A concluída ainda não decidida (sugestão "Gama") não entra: só aceitas e rejeitadas são decisões (RN-12).
        // Pendente e falhas também não entram na taxa, mas aparecem nos contadores.
        (ia.Pendentes, ia.Falhas).ShouldBe((1, 2));
    }

    [Fact]
    public async Task Resumo_MassaControlada_ConsumoDosUltimos30DiasPorOperacaoEModelo()
    {
        var consumo = (await ResumoAsync(await BancoComMassaAsync())).Ia.Consumo30d;

        // A linha de 40 dias atrás fica de fora. p95 de [100, 200, 300] por interpolação = 290.
        consumo.ShouldBe(
        [
            new("embedding", "fake-embedding-v1", Chamadas: 1, Falhas: 0, TokensEntrada: 7, TokensSaida: null,
                LatenciaP95Ms: 50),
            new("triagem", "fake-triagem-v1", Chamadas: 3, Falhas: 1, TokensEntrada: 20, TokensSaida: 10,
                LatenciaP95Ms: 290),
        ]);
    }

    [Fact]
    public async Task Resumo_BancoSemChamados_DevolveZerosETaxaNulaSemDividirPorZero()
    {
        var resumo = await ResumoAsync(await BancoMigradoAsync());

        resumo.TotalChamados.ShouldBe(0);
        resumo.PorStatus.ShouldAllBe(s => s.Total == 0);
        resumo.PorStatus.Count.ShouldBe(5);
        resumo.TempoMedioResolucaoPorCategoria.ShouldAllBe(t => t.Resolvidos == 0 && t.TempoMedioHoras == null);
        resumo.Ia.TaxaAceitacao.ShouldBeNull();
        resumo.Ia.PorCategoria.ShouldBeEmpty();
        resumo.Ia.Consumo30d.ShouldBeEmpty();
    }

    // ---------- Massa controlada ----------

    private async Task<string> BancoMigradoAsync()
    {
        var connectionString = await banco.CriarBancoVazioAsync(Ct);
        await ExecutarAsync(connectionString, async db =>
        {
            await db.Database.MigrateAsync(Ct);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO categorias (id, nome) VALUES (1, 'Alfa'), (2, 'Beta'), (3, 'Gama')", Ct);
        });
        return connectionString;
    }

    private async Task<string> BancoComMassaAsync()
    {
        var connectionString = await BancoMigradoAsync();
        await ExecutarAsync(connectionString, db => db.Database.ExecuteSqlRawAsync("""
            WITH base AS (SELECT now() - interval '20 days' AS t0)
            INSERT INTO chamados (id, titulo, descricao, solicitante_nome, solicitante_email, categoria_id,
                                  prioridade, status, criado_em, atualizado_em, resolvido_em)
            SELECT id::uuid, 'Chamado de teste', 'Descrição do chamado de teste.', 'Pessoa', 'p@example.com',
                   categoria, prioridade::prioridade_chamado, status::status_chamado, t0, t0, t0 + resolucao
            FROM base, (VALUES
                ('00000000-0000-7000-8000-000000000001', 1, 'alta',    'resolvido',    interval '2 hours'),
                ('00000000-0000-7000-8000-000000000002', 1, 'media',   'resolvido',    interval '4 hours'),
                ('00000000-0000-7000-8000-000000000003', 2, 'baixa',   'fechado',      interval '10 hours'),
                ('00000000-0000-7000-8000-000000000004', 2, 'critica', 'aberto',       NULL),
                ('00000000-0000-7000-8000-000000000005', NULL, 'media', 'em_andamento', NULL),
                ('00000000-0000-7000-8000-000000000006', 1, 'baixa',   'cancelado',    NULL)
            ) AS v(id, categoria, prioridade, status, resolucao);

            INSERT INTO triagens_ia (id, chamado_id, status, categoria_sugerida_id, prioridade_sugerida, resumo,
                                     resposta_sugerida, confianca, provedor, modelo, prompt_versao, tentativas,
                                     proxima_tentativa_em, criado_em, concluida_em, decidida_em, decidida_por,
                                     erro_motivo)
            SELECT gen_random_uuid(), chamado::uuid, status::status_triagem, sugerida,
                   CASE WHEN sugerida IS NULL THEN NULL ELSE 'media'::prioridade_chamado END,
                   CASE WHEN sugerida IS NULL THEN NULL ELSE 'Resumo.' END,
                   CASE WHEN sugerida IS NULL THEN NULL ELSE 'Olá!' END,
                   CASE WHEN sugerida IS NULL THEN NULL ELSE 0.8 END,
                   CASE WHEN status = 'pendente' THEN NULL ELSE 'fake' END,
                   CASE WHEN status = 'pendente' THEN NULL ELSE 'fake-triagem-v1' END,
                   CASE WHEN status = 'pendente' THEN NULL ELSE 'triagem.v1' END,
                   0, now(), now(),
                   CASE WHEN status = 'pendente' THEN NULL ELSE now() END,
                   CASE WHEN status IN ('aceita', 'rejeitada') THEN now() END,
                   CASE WHEN status IN ('aceita', 'rejeitada') THEN 'Ana' END,
                   CASE WHEN status = 'falhou' THEN 'Falhou.' END
            FROM (VALUES
                ('00000000-0000-7000-8000-000000000001', 'aceita',    1),
                ('00000000-0000-7000-8000-000000000002', 'aceita',    1),
                ('00000000-0000-7000-8000-000000000006', 'rejeitada', 1),
                ('00000000-0000-7000-8000-000000000003', 'aceita',    2),
                ('00000000-0000-7000-8000-000000000004', 'pendente',  NULL),
                ('00000000-0000-7000-8000-000000000004', 'concluida', 3),
                ('00000000-0000-7000-8000-000000000005', 'falhou',    NULL),
                ('00000000-0000-7000-8000-000000000005', 'falhou',    NULL)
            ) AS v(chamado, status, sugerida);

            INSERT INTO uso_llm (operacao, provedor, modelo, tokens_entrada, tokens_saida, latencia_ms, sucesso,
                                 erro_tipo, criado_em)
            VALUES ('triagem',   'fake', 'fake-triagem-v1',   10,   5,    100,  true,  NULL,      now()),
                   ('triagem',   'fake', 'fake-triagem-v1',   10,   5,    200,  true,  NULL,      now()),
                   ('triagem',   'fake', 'fake-triagem-v1',   NULL, NULL, 300,  false, 'timeout', now()),
                   ('embedding', 'fake', 'fake-embedding-v1', 7,    NULL, 50,   true,  NULL,      now()),
                   ('triagem',   'fake', 'fake-triagem-v1',   99,   99,   9999, true,  NULL,      now() - interval '40 days');
            """, Ct));
        return connectionString;
    }

    private async Task<ResumoDashboard> ResumoAsync(string connectionString)
    {
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<IConsultaDashboard>().ObterResumoAsync(Ct);
    }

    private async Task ExecutarAsync(string connectionString, Func<HelpDeskDbContext, Task> acao)
    {
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        await acao(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }
}
