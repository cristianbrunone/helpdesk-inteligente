using System.Collections.Concurrent;
using System.Diagnostics;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Observabilidade;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using HelpDesk.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Worker;

/// <summary>
/// ADR-0019: o processamento de uma triagem é um trace próprio, vinculado ao da criação, com um span por etapa e
/// por tentativa ao provedor, e nenhum atributo com conteúdo do chamado (nem no SQL do Npgsql).
/// </summary>
public sealed class TracingTriagemTests(BancoFixture banco)
{
    private const string Cpf = "529.982.247-25";
    private const string Nome = "Mariana Quitéria";
    private static readonly DateTimeOffset _inicio = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Processar_Triagem_GeraTraceVinculadoAoDaCriacaoComEtapasESemConteudo()
    {
        var spans = new ConcurrentBag<Activity>();
        using var ouvinte = new ActivityListener
        {
            ShouldListenTo = fonte => Tracing.Fontes.Contains(fonte.Name) || fonte.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(ouvinte);

        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var criacao = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded);
        var triagemId = await CriarPendenteAsync(bancoIsolado, $"00-{criacao.TraceId}-{criacao.SpanId}-01");
        await using var servicos = Servicos(bancoIsolado);

        await servicos.GetRequiredService<ConsumidorFilaTriagem>().ProcessarLoteAsync(Ct);

        var raiz = spans.Single(s => s.OperationName == "triagem.processar" && Equals(s.GetTagItem("triagem.id"), triagemId));
        raiz.ParentSpanId.ShouldBe(default);
        raiz.TraceId.ShouldNotBe(criacao.TraceId);
        raiz.Links.ShouldContain(l => l.Context.TraceId == criacao.TraceId && l.Context.SpanId == criacao.SpanId);
        raiz.GetTagItem("triagem.status").ShouldBe("Concluida");

        var doTrace = spans.Where(s => s.TraceId == raiz.TraceId).ToList();
        var nomes = doTrace.Select(s => s.OperationName).ToHashSet();
        foreach (var etapa in new[] { "mascarar", "recuperar", "montar_prompt", "completar", "validar", "llm.tentativa" })
        {
            nomes.ShouldContain(etapa);
        }

        var chat = doTrace.Where(s => s.Source.Name == Tracing.FonteChat).ShouldHaveSingleItem();
        chat.Parent!.OperationName.ShouldBe("llm.tentativa");
        chat.GetTagItem("gen_ai.request.model").ShouldNotBeNull();
        doTrace.ShouldContain(s => s.Source.Name == "Npgsql");

        // O texto do chamado tem CPF e o nome do solicitante: nada disso pode estar em nenhum atributo.
        var atributos = doTrace.SelectMany(s => s.TagObjects).ToList();
        atributos.ShouldNotBeEmpty();
        atributos.ShouldAllBe(a => !(a.Value ?? "").ToString()!.Contains(Cpf)
            && !(a.Value ?? "").ToString()!.Contains("Mariana")
            && !(a.Value ?? "").ToString()!.Contains("boleto"));
        // Com EnableSensitiveData ligado, o middleware gravaria o prompt e a resposta nestes atributos (convenção
        // GenAI do OpenTelemetry). Metadados como "gen_ai.output.type" (= json) continuam permitidos.
        string[] atributosDeConteudo = ["gen_ai.input.messages", "gen_ai.output.messages", "gen_ai.system_instructions"];
        atributos.ShouldNotContain(a => atributosDeConteudo.Contains(a.Key));
        chat.GetTagItem("gen_ai.usage.input_tokens").ShouldNotBeNull();
    }

    private static ServiceProvider Servicos(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AdicionarTriagem(connectionString, new OpcoesIA(true, true),
            new OpcoesLlm
            {
                Provedor = TipoProvedorLlm.Fake,
                ModeloChat = "x",
                Timeout = TimeSpan.FromSeconds(10),
                MaxRetries = 0,
                MaxTokensSaidaTriagem = 800,
            },
            new OpcoesFila(5, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5), OpcoesFila.MaxReservasPadrao));
        return services.BuildServiceProvider();
    }

    private async Task<Guid> CriarPendenteAsync(string connectionString, string traceParent)
    {
        var chamado = Chamado.Abrir("Não consigo emitir o boleto",
            $"Aqui é a {Nome}. O boleto não sai. Meu CPF é {Cpf}.", Nome, "mariana@example.com", null, null, _inicio);
        var triagem = TriagemIA.Criar(chamado, _inicio, traceParent);
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Chamados.Add(chamado);
        db.Triagens.Add(triagem);
        await db.SaveChangesAsync(Ct);
        return triagem.Id;
    }
}
