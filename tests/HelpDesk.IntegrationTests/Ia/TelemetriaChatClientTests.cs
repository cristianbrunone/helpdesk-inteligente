using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.IntegrationTests.Ia;

/// <summary>O cliente montado como na aplicação: resiliência → telemetria → fake, gravando no PostgreSQL real.</summary>
public sealed class TelemetriaChatClientTests(BancoFixture banco)
{
    private const string ConteudoSecreto = "CONTEUDO-QUE-NAO-PODE-IR-PARA-O-LOG";
    private static readonly DateTimeOffset _inicio = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChatMessage[] Mensagens() =>
    [
        new(ChatRole.System, "Categorias:\n- Financeiro"),
        new(ChatRole.User, $"<chamado>\nTítulo: Erro no boleto\nDescrição: {ConteudoSecreto}\n</chamado>"),
    ];

    [Fact]
    public async Task Chamada_ComSucesso_GravaUmRegistroComTokensELogSemConteudo()
    {
        var (chamadoId, triagemId) = await CriarTriagemAsync();
        var logs = new LogsCapturados();
        await using var servicos = Servicos(Opcoes(), logs);

        await servicos.GetRequiredService<IChatClient>()
            .GetResponseAsync(Mensagens(), new ChatOptions().ParaTriagem(triagemId, chamadoId), Ct);

        var registro = (await RegistrosAsync(triagemId)).ShouldHaveSingleItem();
        registro.Operacao.ShouldBe("triagem");
        registro.ChamadoId.ShouldBe(chamadoId);
        registro.Provedor.ShouldBe("fake");
        registro.Modelo.ShouldBe("fake-triagem-v1");
        registro.TokensEntrada.ShouldNotBeNull().ShouldBeGreaterThan(0);
        registro.TokensSaida.ShouldNotBeNull().ShouldBeGreaterThan(0);
        registro.LatenciaMs.ShouldBeGreaterThanOrEqualTo(0);
        registro.Sucesso.ShouldBeTrue();
        registro.ErroTipo.ShouldBeNull();

        var log = logs.Registros
            .Where(r => r.Mensagem.StartsWith("Chamada ao LLM", StringComparison.Ordinal))
            .ShouldHaveSingleItem();
        log.Mensagem.ShouldContain($"triagem {triagemId}");
        logs.Registros.ShouldAllBe(r => !r.Mensagem.Contains(ConteudoSecreto));
    }

    [Fact]
    public async Task Chamada_ProvedorEmRateLimit_GravaUmRegistroPorTentativaELancaIndisponivel()
    {
        var (chamadoId, triagemId) = await CriarTriagemAsync();
        await using var servicos = Servicos(Opcoes(ModoFake.RateLimit, maxRetries: 1), new LogsCapturados());

        var erro = await Should.ThrowAsync<ProvedorIndisponivelException>(() => servicos
            .GetRequiredService<IChatClient>()
            .GetResponseAsync(Mensagens(), new ChatOptions().ParaTriagem(triagemId, chamadoId), Ct));

        erro.Tipo.ShouldBe("rate_limit");
        var registros = await RegistrosAsync(triagemId);
        registros.Count.ShouldBe(2);
        registros.ShouldAllBe(r => !r.Sucesso && r.ErroTipo == "rate_limit" && r.TokensEntrada == null);
    }

    [Fact]
    public async Task Chamada_BancoDeTelemetriaFora_NaoDerrubaAChamada()
    {
        var logs = new LogsCapturados();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs));
        services.AdicionarInfraestrutura("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1");
        services.AdicionarClienteLlm(Opcoes());
        await using var servicos = services.BuildServiceProvider();

        var resposta = await servicos.GetRequiredService<IChatClient>()
            .GetResponseAsync(Mensagens(), new ChatOptions().ParaTriagem(Guid.CreateVersion7(), Guid.CreateVersion7()), Ct);

        resposta.Text.ShouldNotBeNullOrWhiteSpace();
        logs.Registros.ShouldContain(r => r.Mensagem.StartsWith("Não foi possível registrar o uso do LLM", StringComparison.Ordinal));
    }

    // ---------- Apoio ----------

    private static OpcoesLlm Opcoes(ModoFake modo = ModoFake.Normal, int maxRetries = 0) => new()
    {
        Provedor = TipoProvedorLlm.Fake,
        ModeloChat = "nao-usado",
        Timeout = TimeSpan.FromSeconds(5),
        MaxRetries = maxRetries,
        MaxTokensSaidaTriagem = 800,
        ModoFake = modo,
    };

    private ServiceProvider Servicos(OpcoesLlm opcoes, LogsCapturados logs)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs));
        services.AdicionarInfraestrutura(banco.ConnectionString);
        services.AdicionarClienteLlm(opcoes);
        return services.BuildServiceProvider();
    }

    private async Task<(Guid ChamadoId, Guid TriagemId)> CriarTriagemAsync()
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo", "maria@example.com", null, null, _inicio);
        var triagem = TriagemIA.Criar(chamado, _inicio);
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Chamados.Add(chamado);
        db.Triagens.Add(triagem);
        await db.SaveChangesAsync(Ct);
        return (chamado.Id, triagem.Id);
    }

    private async Task<List<RegistroUsoLlm>> RegistrosAsync(Guid triagemId)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>()
            .UsoLlm.Where(u => u.TriagemId == triagemId).ToListAsync(Ct);
    }
}
