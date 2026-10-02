using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Api;

/// <summary>A triagem nasce com o chamado (RF-02) e aparece no detalhe e na lista; o kill switch a impede (ADR-0021).</summary>
public sealed class TriagemNoChamadoTests(ApiFactory api, BancoFixture banco) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset _inicio = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly ExecucaoTriagem _execucao = new("fake", "fake-triagem-v1", "triagem.v1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Criar_TriagemHabilitada_DevolveTriagemPendenteGravadaNaMesmaOperacao()
    {
        using var resposta = await api.CriarClienteAtendente().PostAsJsonAsync("/api/chamados", NovoChamado(), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var chamado = await LerAsync(resposta);
        var triagem = chamado.GetProperty("triagem");
        triagem.GetProperty("status").GetString().ShouldBe("Pendente");
        triagem.GetProperty("totalTriagens").GetInt32().ShouldBe(1);
        triagem.GetProperty("fontes").GetArrayLength().ShouldBe(0);
        triagem.GetProperty("categoriaSugerida").ValueKind.ShouldBe(JsonValueKind.Null);

        var id = chamado.GetProperty("id").GetGuid();
        var pendentes = await ConsultarAsync(db =>
            db.Triagens.CountAsync(t => t.ChamadoId == id && t.Status == StatusTriagem.Pendente, Ct));
        pendentes.ShouldBe(1);
    }

    [Fact]
    public async Task Listar_ChamadoRecemCriado_TrazOStatusDaTriagemVigente()
    {
        var marcador = $"zt{Guid.NewGuid():N}"[..12];
        using var criacao = await api.CriarClienteAtendente()
            .PostAsJsonAsync("/api/chamados", NovoChamado($"Erro ao emitir boleto {marcador}"), Ct);

        using var lista = await api.CriarClienteAtendente().GetAsync($"/api/chamados?q={marcador}", Ct);

        var item = (await LerAsync(lista)).GetProperty("itens").EnumerateArray().ShouldHaveSingleItem();
        item.GetProperty("triagemStatus").GetString().ShouldBe("Pendente");
    }

    [Fact]
    public async Task Obter_ComVariasTriagens_MostraAVigenteComSugestaoETotal()
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo", "maria@example.com", null, null, _inicio);
        var antiga = TriagemIA.Criar(chamado, _inicio);
        antiga.Falhar("A IA retornou uma resposta fora do formato esperado.", _execucao, _inicio.AddSeconds(3));
        var vigente = TriagemIA.Refazer(chamado, antiga, _inicio.AddMinutes(1));
        vigente.Concluir(new SugestaoTriagem(2, Prioridade.Alta, "Erro 403 em boletos.", "Olá! Vamos verificar.", 0.82m),
            _execucao, _inicio.AddMinutes(1).AddSeconds(4));
        await ConsultarAsync(async db =>
        {
            db.Chamados.Add(chamado);
            db.Triagens.AddRange(antiga, vigente);
            return await db.SaveChangesAsync(Ct);
        });

        using var resposta = await api.CriarClienteAtendente().GetAsync($"/api/chamados/{chamado.Id}", Ct);

        var triagem = (await LerAsync(resposta)).GetProperty("triagem");
        triagem.GetProperty("id").GetGuid().ShouldBe(vigente.Id);
        triagem.GetProperty("status").GetString().ShouldBe("Concluida");
        triagem.GetProperty("categoriaSugerida").GetProperty("nome").GetString().ShouldBe("Financeiro");
        triagem.GetProperty("prioridadeSugerida").GetString().ShouldBe("Alta");
        triagem.GetProperty("confianca").GetDecimal().ShouldBe(0.82m);
        triagem.GetProperty("modelo").GetString().ShouldBe("fake-triagem-v1");
        triagem.GetProperty("promptVersao").GetString().ShouldBe("triagem.v1");
        triagem.GetProperty("erro").ValueKind.ShouldBe(JsonValueKind.Null);
        triagem.GetProperty("totalTriagens").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Obter_TriagemQueFalhou_TrazAMensagemAmigavel()
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo", "maria@example.com", null, null, _inicio);
        var triagem = TriagemIA.Criar(chamado, _inicio);
        triagem.Falhar("O provedor de IA não respondeu a tempo. Tente refazer a triagem.", _execucao, _inicio);
        await ConsultarAsync(async db =>
        {
            db.Chamados.Add(chamado);
            db.Triagens.Add(triagem);
            return await db.SaveChangesAsync(Ct);
        });

        using var resposta = await api.CriarClienteAtendente().GetAsync($"/api/chamados/{chamado.Id}", Ct);

        var detalhe = (await LerAsync(resposta)).GetProperty("triagem");
        detalhe.GetProperty("status").GetString().ShouldBe("Falhou");
        detalhe.GetProperty("erro").GetString().ShouldBe("O provedor de IA não respondeu a tempo. Tente refazer a triagem.");
    }

    [Fact]
    public async Task ConfigIA_PorPadrao_InformaTriagemECopilotoAtivos()
    {
        using var resposta = await api.CriarClienteAtendente().GetAsync("/api/config/ia", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LerAsync(resposta)).GetRawText().ShouldBe("""{"triagem":true,"copiloto":true}""");
    }

    [Fact]
    public async Task Criar_TriagemDesativada_CriaOChamadoSemTriagemENadaNaFila()
    {
        await using var semTriagem = api.WithWebHostBuilder(b => b.UseSetting("IA_TRIAGEM_HABILITADA", "false"));
        var cliente = semTriagem.CriarClienteAtendente();

        using var config = await cliente.GetAsync("/api/config/ia", Ct);
        using var resposta = await cliente.PostAsJsonAsync("/api/chamados", NovoChamado(), Ct);

        (await LerAsync(config)).GetProperty("triagem").GetBoolean().ShouldBeFalse();
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var chamado = await LerAsync(resposta);
        chamado.GetProperty("triagem").ValueKind.ShouldBe(JsonValueKind.Null);
        var id = chamado.GetProperty("id").GetGuid();
        (await ConsultarAsync(db => db.Triagens.CountAsync(t => t.ChamadoId == id, Ct))).ShouldBe(0);
    }

    // ---------- Apoio ----------

    private static object NovoChamado(string titulo = "Erro ao emitir boleto") => new
    {
        titulo,
        descricao = "Desde ontem aparece erro 403 no módulo de boletos.",
        solicitanteNome = "Maria Exemplo",
        solicitanteEmail = "maria@example.com",
    };

    private static async Task<JsonElement> LerAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return json.RootElement.Clone();
    }

    private async Task<T> ConsultarAsync<T>(Func<HelpDeskDbContext, Task<T>> consulta)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }
}
