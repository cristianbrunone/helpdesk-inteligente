using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using HelpDesk.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HelpDesk.IntegrationTests.Worker;

/// <summary>
/// A fila de triagem contra o PostgreSQL real, cada teste no seu banco isolado: o consumidor processa todas as
/// pendentes que encontra, e não pode mexer nas triagens dos outros testes.
/// </summary>
public sealed class FilaTriagemTests(BancoFixture banco, ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset _inicio = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Processamento (critérios de aceite) ----------

    [Fact]
    public async Task ProcessarLote_TriagemPendenteComFake_FicaConcluidaComUsoRegistrado()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var triagemId = await CriarPendenteAsync(bancoIsolado);
        await using var worker = Worker(bancoIsolado);

        var processadas = await worker.Consumidor.ProcessarLoteAsync(Ct);

        processadas.ShouldBe(1);
        var triagem = await LerAsync(bancoIsolado, db => db.Triagens.SingleAsync(t => t.Id == triagemId, Ct));
        triagem.Status.ShouldBe(StatusTriagem.Concluida);
        triagem.CategoriaSugeridaId.ShouldBe((short)2); // "boleto" → Financeiro
        triagem.LockExpiraEm.ShouldBeNull();
        (await LerAsync(bancoIsolado, db => db.UsoLlm.CountAsync(u => u.TriagemId == triagemId, Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task ProcessarLote_FakeComJsonInvalido_FicaFalhouComMotivoEOWorkerSegue()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var triagemId = await CriarPendenteAsync(bancoIsolado);
        await using var worker = Worker(bancoIsolado, Llm(ModoFake.JsonInvalido));

        await worker.Consumidor.ProcessarLoteAsync(Ct);

        var triagem = await LerAsync(bancoIsolado, db => db.Triagens.SingleAsync(t => t.Id == triagemId, Ct));
        triagem.Status.ShouldBe(StatusTriagem.Falhou);
        triagem.ErroMotivo.ShouldBe("A IA retornou uma resposta fora do formato esperado.");
        (await worker.Consumidor.ProcessarLoteAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task ProcessarLote_ProvedorLento_TentaDeNovoEFicaFalhouPorTimeout()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var triagemId = await CriarPendenteAsync(bancoIsolado);
        await using var worker = Worker(bancoIsolado, new OpcoesLlm
        {
            Provedor = TipoProvedorLlm.Fake,
            ModeloChat = "x",
            Timeout = TimeSpan.FromMilliseconds(200),
            MaxRetries = 1,
            MaxTokensSaidaTriagem = 800,
            ModoFake = ModoFake.Lento,
            AtrasoFake = TimeSpan.FromSeconds(30),
        });

        await worker.Consumidor.ProcessarLoteAsync(Ct);

        var triagem = await LerAsync(bancoIsolado, db => db.Triagens.SingleAsync(t => t.Id == triagemId, Ct));
        triagem.Status.ShouldBe(StatusTriagem.Falhou);
        triagem.ErroMotivo.ShouldBe("O provedor de IA não respondeu a tempo. Tente refazer a triagem.");
        var tentativas = await LerAsync(bancoIsolado, db => db.UsoLlm.Where(u => u.TriagemId == triagemId).ToListAsync(Ct));
        tentativas.Count.ShouldBe(2);
        tentativas.ShouldAllBe(u => !u.Sucesso && u.ErroTipo == "timeout");
    }

    // ---------- Concorrência e lease (ADR-0003, ADR-0010) ----------

    [Fact]
    public async Task Reservar_DuasInstanciasAoMesmoTempo_NuncaRecebemAMesmaTriagem()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        for (var i = 0; i < 20; i++)
        {
            await CriarPendenteAsync(bancoIsolado);
        }

        await using var a = Worker(bancoIsolado);
        await using var b = Worker(bancoIsolado);
        var reservas = await Task.WhenAll(ReservarAsync(a, 15), ReservarAsync(b, 15));

        reservas[0].Intersect(reservas[1]).ShouldBeEmpty();
        reservas[0].Concat(reservas[1]).Distinct().Count().ShouldBe(20);
    }

    [Fact]
    public async Task Reservar_LinhasTravadasPorOutraInstancia_PulaSemEsperar()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        for (var i = 0; i < 5; i++)
        {
            await CriarPendenteAsync(bancoIsolado);
        }

        // Outra instância segura 2 pendentes numa transação aberta (como no meio de uma reserva).
        await using var conexao = new NpgsqlConnection(bancoIsolado);
        await conexao.OpenAsync(Ct);
        await using var transacao = await conexao.BeginTransactionAsync(Ct);
        var travadas = new List<Guid>();
        await using (var comando = new NpgsqlCommand(
            "SELECT id FROM triagens_ia WHERE status = 'pendente' ORDER BY proxima_tentativa_em LIMIT 2 FOR UPDATE",
            conexao, transacao))
        await using (var leitor = await comando.ExecuteReaderAsync(Ct))
        {
            while (await leitor.ReadAsync(Ct))
            {
                travadas.Add(leitor.GetGuid(0));
            }
        }

        await using var worker = Worker(bancoIsolado);
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limite.CancelAfter(TimeSpan.FromSeconds(5)); // sem SKIP LOCKED, a reserva ficaria bloqueada aqui
        await using var escopo = worker.Servicos.CreateAsyncScope();
        var reservadas = await escopo.ServiceProvider.GetRequiredService<IFilaTriagem>()
            .ReservarAsync(10, TimeSpan.FromMinutes(5), limite.Token);

        reservadas.Count.ShouldBe(3);
        reservadas.Intersect(travadas).ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessarLote_DoisWorkersConcorrentes_ProcessamCadaTriagemUmaVezSo()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var criadas = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            criadas.Add(await CriarPendenteAsync(bancoIsolado));
        }

        await using var a = Worker(bancoIsolado, fila: new OpcoesFila(3, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5), 3));
        await using var b = Worker(bancoIsolado, fila: new OpcoesFila(3, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5), 3));
        await Task.WhenAll(EsvaziarAsync(a), EsvaziarAsync(b));

        // O seed também tem triagens (já decididas ou falhas); o teste olha só as que criou.
        var status = await LerAsync(bancoIsolado, db => db.Triagens
            .Where(t => criadas.Contains(t.Id)).Select(t => t.Status).ToListAsync(Ct));
        status.Count.ShouldBe(12);
        status.ShouldAllBe(s => s == StatusTriagem.Concluida);
        // Só as chamadas de chat: o embedding da consulta do RAG também vai para o uso_llm, sem triagem_id.
        var chamadasPorTriagem = await LerAsync(bancoIsolado, db => db.UsoLlm
            .Where(u => u.Operacao == RegistroUsoLlm.OperacaoTriagem)
            .GroupBy(u => u.TriagemId).Select(g => g.Count()).ToListAsync(Ct));
        chamadasPorTriagem.Count.ShouldBe(12);
        chamadasPorTriagem.ShouldAllBe(n => n == 1);
    }

    [Fact]
    public async Task Reservar_LeaseExpirado_RetomaATriagemContandoATentativa()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var triagemId = await CriarPendenteAsync(bancoIsolado);
        await using var worker = Worker(bancoIsolado);

        (await ReservarAsync(worker, 5)).ShouldBe([triagemId]);
        (await ReservarAsync(worker, 5)).ShouldBeEmpty(); // lease ativo: ninguém mais pega
        await ExpirarLeaseAsync(bancoIsolado, triagemId);
        var retomada = await ReservarAsync(worker, 5);

        retomada.ShouldBe([triagemId]);
        var triagem = await LerAsync(bancoIsolado, db => db.Triagens.SingleAsync(t => t.Id == triagemId, Ct));
        triagem.Tentativas.ShouldBe((short)2);
        triagem.LockExpiraEm.ShouldNotBeNull();
        triagem.ProximaTentativaEm.ShouldBeGreaterThan(triagem.LockExpiraEm.Value); // backoff exponencial
    }

    [Fact]
    public async Task ProcessarLote_TriagemInterrompidaVariasVezes_FalhaComoDeadLetter()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var triagemId = await CriarPendenteAsync(bancoIsolado);
        await LerAsync(bancoIsolado, db => db.Database.ExecuteSqlAsync(
            $"UPDATE triagens_ia SET tentativas = {OpcoesFila.MaxReservasPadrao} WHERE id = {triagemId}", Ct));
        await using var worker = Worker(bancoIsolado);

        await worker.Consumidor.ProcessarLoteAsync(Ct);

        var triagem = await LerAsync(bancoIsolado, db => db.Triagens.SingleAsync(t => t.Id == triagemId, Ct));
        triagem.Status.ShouldBe(StatusTriagem.Falhou);
        triagem.ErroMotivo.ShouldBe("O processamento desta triagem foi interrompido várias vezes. Tente refazer.");
        (await LerAsync(bancoIsolado, db => db.UsoLlm.CountAsync(u => u.TriagemId == triagemId, Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Reservar_ProximaTentativaNoFuturo_AindaNaoReserva()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var triagemId = await CriarPendenteAsync(bancoIsolado);
        await LerAsync(bancoIsolado, db => db.Database.ExecuteSqlAsync(
            $"UPDATE triagens_ia SET proxima_tentativa_em = now() + interval '1 hour' WHERE id = {triagemId}", Ct));
        await using var worker = Worker(bancoIsolado);

        (await ReservarAsync(worker, 5)).ShouldBeEmpty();
    }

    // ---------- Kill switch (ADR-0021) ----------

    [Fact]
    public async Task Worker_TriagemDesativada_NaoConsomeAFila()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var triagemId = await CriarPendenteAsync(bancoIsolado);
        await using var worker = Worker(bancoIsolado, opcoesIA: new OpcoesIA(TriagemHabilitada: false, CopilotoHabilitado: true));

        await worker.Consumidor.StartAsync(Ct);
        await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        await worker.Consumidor.StopAsync(Ct);

        var triagem = await LerAsync(bancoIsolado, db => db.Triagens.SingleAsync(t => t.Id == triagemId, Ct));
        triagem.Status.ShouldBe(StatusTriagem.Pendente);
        triagem.Tentativas.ShouldBe((short)0);
    }

    // ---------- Critério: criar não espera a IA ----------

    [Fact]
    public async Task CriarChamado_ComProvedorDe30sProcessandoAFila_RespondeEmMenosDe300ms()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await CriarPendenteAsync(bancoIsolado); // o Worker fica ocupado com esta, no provedor lento
        await using var worker = Worker(bancoIsolado, Llm(ModoFake.Lento));
        await worker.Consumidor.StartAsync(Ct);
        await using var apiIsolada = api.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Default", bancoIsolado));
        var cliente = apiIsolada.CriarClienteAtendente();
        using (await cliente.GetAsync("/api/categorias", Ct))
        {
            // Aquece a API (JIT, pool de conexões): mede-se a criação, não a primeira requisição do processo.
        }

        var cronometro = Stopwatch.StartNew();
        using var resposta = await cliente.PostAsJsonAsync("/api/chamados", new
        {
            titulo = "Erro ao emitir boleto",
            descricao = "Desde ontem aparece erro 403 no módulo de boletos.",
            solicitanteNome = "Maria Exemplo",
            solicitanteEmail = "maria@example.com",
        }, Ct);
        cronometro.Stop();
        await worker.Consumidor.StopAsync(Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        cronometro.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(300));
    }

    // ---------- Apoio ----------

    private static OpcoesLlm Llm(ModoFake modo = ModoFake.Normal) => new()
    {
        Provedor = TipoProvedorLlm.Fake,
        ModeloChat = "x",
        Timeout = TimeSpan.FromSeconds(60),
        MaxRetries = 0,
        MaxTokensSaidaTriagem = 800,
        ModoFake = modo,
        AtrasoFake = TimeSpan.FromSeconds(30),
    };

    private static WorkerDeTeste Worker(
        string connectionString, OpcoesLlm? llm = null, OpcoesIA? opcoesIA = null, OpcoesFila? fila = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AdicionarTriagem(connectionString, opcoesIA ?? new OpcoesIA(true, true), llm ?? Llm(),
            fila ?? new OpcoesFila(10, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5), OpcoesFila.MaxReservasPadrao));
        return new WorkerDeTeste(services.BuildServiceProvider());
    }

    private static async Task<IReadOnlyList<Guid>> ReservarAsync(WorkerDeTeste worker, int quantidade)
    {
        await using var escopo = worker.Servicos.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<IFilaTriagem>()
            .ReservarAsync(quantidade, TimeSpan.FromMinutes(5), Ct);
    }

    private static async Task EsvaziarAsync(WorkerDeTeste worker)
    {
        while (await worker.Consumidor.ProcessarLoteAsync(Ct) > 0)
        {
        }
    }

    private async Task<Guid> CriarPendenteAsync(string connectionString)
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Não consigo emitir o boleto do mês.",
            "Maria Exemplo", "maria@example.com", null, null, _inicio);
        var triagem = TriagemIA.Criar(chamado, _inicio);
        await LerAsync(connectionString, async db =>
        {
            db.Chamados.Add(chamado);
            db.Triagens.Add(triagem);
            return await db.SaveChangesAsync(Ct);
        });
        return triagem.Id;
    }

    private Task ExpirarLeaseAsync(string connectionString, Guid triagemId) =>
        LerAsync(connectionString, db => db.Database.ExecuteSqlAsync($"""
            UPDATE triagens_ia SET lock_expira_em = now() - interval '1 second',
                                   proxima_tentativa_em = now() - interval '1 second'
            WHERE id = {triagemId}
            """, Ct));

    private async Task<T> LerAsync<T>(string connectionString, Func<HelpDeskDbContext, Task<T>> consulta)
    {
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }

    private sealed class WorkerDeTeste(ServiceProvider servicos) : IAsyncDisposable
    {
        public ServiceProvider Servicos { get; } = servicos;

        public ConsumidorFilaTriagem Consumidor => Servicos.GetRequiredService<ConsumidorFilaTriagem>();

        public ValueTask DisposeAsync() => Servicos.DisposeAsync();
    }
}
