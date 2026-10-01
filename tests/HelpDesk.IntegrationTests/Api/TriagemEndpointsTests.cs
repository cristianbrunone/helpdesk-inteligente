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

public sealed class TriagemEndpointsTests(ApiFactory api, BancoFixture banco) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset _inicio = new(2026, 7, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly ExecucaoTriagem _execucao = new("fake", "fake-triagem-v1", "triagem.v1");
    private static readonly SugestaoTriagem _sugestao =
        new(2, Prioridade.Alta, "Erro 403 em boletos.", "Olá! Vamos verificar.", 0.82m);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Refazer (RF-12) ----------

    [Fact]
    public async Task Refazer_TriagemQueFalhou_Retorna202ComNovaPendenteEPreservaAAnterior()
    {
        var id = await CriarAsync(StatusTriagem.Falhou);

        using var resposta = await api.CreateClient().PostAsync($"/api/chamados/{id}/triagem", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        resposta.Headers.Location!.ToString().ShouldBe($"/api/chamados/{id}");
        var triagem = await LerAsync(resposta);
        triagem.GetProperty("status").GetString().ShouldBe("Pendente");
        triagem.GetProperty("totalTriagens").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Refazer_ComTriagemPendente_Retorna409TriagemEmAndamento()
    {
        var id = await CriarAsync(StatusTriagem.Pendente);

        using var resposta = await api.CreateClient().PostAsync($"/api/chamados/{id}/triagem", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("triagem_em_andamento");
    }

    [Fact]
    public async Task Refazer_DuasVezesAoMesmoTempo_UmaCriaEAOutraRecebe409()
    {
        var id = await CriarAsync(StatusTriagem.Falhou);
        var cliente = api.CreateClient();

        var respostas = await Task.WhenAll(
            cliente.PostAsync($"/api/chamados/{id}/triagem", null, Ct),
            cliente.PostAsync($"/api/chamados/{id}/triagem", null, Ct));

        respostas.Select(r => r.StatusCode).ShouldBe([HttpStatusCode.Accepted, HttpStatusCode.Conflict], ignoreOrder: true);
        foreach (var resposta in respostas)
        {
            resposta.Dispose();
        }
    }

    [Fact]
    public async Task Refazer_ChamadoFinalizado_Retorna409ChamadoFinalizado()
    {
        var id = await CriarAsync(StatusTriagem.Falhou, finalizar: true);

        using var resposta = await api.CreateClient().PostAsync($"/api/chamados/{id}/triagem", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("chamado_finalizado");
    }

    [Fact]
    public async Task Refazer_ChamadoInexistente_Retorna404()
    {
        using var resposta = await api.CreateClient().PostAsync($"/api/chamados/{Guid.CreateVersion7()}/triagem", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Refazer_TriagemDesativada_Retorna503IaIndisponivel()
    {
        var id = await CriarAsync(StatusTriagem.Falhou);
        await using var semTriagem = api.WithWebHostBuilder(b => b.UseSetting("IA_TRIAGEM_HABILITADA", "false"));

        using var resposta = await semTriagem.CreateClient().PostAsync($"/api/chamados/{id}/triagem", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var problema = await LerAsync(resposta);
        problema.GetProperty("codigo").GetString().ShouldBe("ia_indisponivel");
        problema.GetProperty("detail").GetString().ShouldBe("A triagem por IA está desativada no momento.");
    }

    // ---------- Aceitar (RF-13) ----------

    [Fact]
    public async Task Aceitar_TriagemConcluida_AplicaCategoriaEPrioridadeEMudaOETag()
    {
        var id = await CriarAsync(StatusTriagem.Concluida);
        var etagAntes = await ETagAsync(id);

        using var resposta = await DecidirAsync(id, "aceitar", new { decididaPor = "Ana (suporte)" }, etagAntes);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        resposta.Headers.ETag!.ToString().ShouldNotBe(etagAntes);
        var chamado = await LerAsync(resposta);
        chamado.GetProperty("categoria").GetProperty("nome").GetString().ShouldBe("Financeiro");
        chamado.GetProperty("prioridade").GetString().ShouldBe("Alta");
        var triagem = chamado.GetProperty("triagem");
        triagem.GetProperty("status").GetString().ShouldBe("Aceita");
        triagem.GetProperty("decididaPor").GetString().ShouldBe("Ana (suporte)");
    }

    [Fact]
    public async Task Aceitar_IfMatchDesatualizado_Retorna412ENaoAplica()
    {
        var id = await CriarAsync(StatusTriagem.Concluida);
        var etagLido = await ETagAsync(id);
        using (await api.CreateClient().PostAsJsonAsync($"/api/chamados/{id}/comentarios",
                   new { autor = "Bruno", texto = "Alterei antes." }, Ct))
        {
        }

        using var resposta = await DecidirAsync(id, "aceitar", new { decididaPor = "Ana" }, etagLido);

        resposta.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await LerTriagemAsync(id)).Status.ShouldBe(StatusTriagem.Concluida);
    }

    [Theory]
    [InlineData(StatusTriagem.Pendente)]
    [InlineData(StatusTriagem.Falhou)]
    [InlineData(StatusTriagem.Aceita)]
    public async Task Aceitar_TriagemNaoConcluida_Retorna409(StatusTriagem status)
    {
        var id = await CriarAsync(status);

        using var resposta = await DecidirAsync(id, "aceitar", new { decididaPor = "Ana" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("triagem_nao_concluida");
    }

    [Fact]
    public async Task Aceitar_ChamadoFinalizado_Retorna409ChamadoFinalizado()
    {
        var id = await CriarAsync(StatusTriagem.Concluida, finalizar: true);

        using var resposta = await DecidirAsync(id, "aceitar", new { decididaPor = "Ana" });

        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("chamado_finalizado");
    }

    [Fact]
    public async Task Aceitar_ChamadoSemTriagem_Retorna404()
    {
        var id = await CriarAsync(status: null);

        using var resposta = await DecidirAsync(id, "aceitar", new { decididaPor = "Ana" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LerAsync(resposta)).GetProperty("detail").GetString().ShouldBe("Este chamado não tem triagem.");
    }

    [Fact]
    public async Task Aceitar_SemQuemDecidiu_Retorna422()
    {
        var id = await CriarAsync(StatusTriagem.Concluida);

        using var resposta = await DecidirAsync(id, "aceitar", new { decididaPor = " " });

        resposta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LerAsync(resposta)).GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["decididaPor"]);
    }

    // ---------- Rejeitar (RF-14) ----------

    [Fact]
    public async Task Rejeitar_TriagemConcluida_NaoAlteraOChamadoEGuardaOMotivo()
    {
        var id = await CriarAsync(StatusTriagem.Concluida);

        using var resposta = await DecidirAsync(id, "rejeitar",
            new { decididaPor = "Ana", motivo = "Categoria correta é Bug no sistema" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chamado = await LerAsync(resposta);
        chamado.GetProperty("categoria").ValueKind.ShouldBe(JsonValueKind.Null);
        chamado.GetProperty("prioridade").GetString().ShouldBe("Media");
        chamado.GetProperty("triagem").GetProperty("status").GetString().ShouldBe("Rejeitada");
        (await LerTriagemAsync(id)).MotivoRejeicao.ShouldBe("Categoria correta é Bug no sistema");
    }

    [Fact]
    public async Task Rejeitar_TriagemJaAceita_Retorna409()
    {
        var id = await CriarAsync(StatusTriagem.Aceita);

        using var resposta = await DecidirAsync(id, "rejeitar", new { decididaPor = "Ana" });

        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("triagem_nao_concluida");
    }

    // ---------- Apoio ----------

    /// <summary>Chamado com a triagem vigente no status pedido (ou sem triagem).</summary>
    private async Task<Guid> CriarAsync(StatusTriagem? status, bool finalizar = false)
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo", "maria@example.com", null, null, _inicio);
        TriagemIA? triagem = null;
        if (status is { } s)
        {
            triagem = TriagemIA.Criar(chamado, _inicio);
            if (s == StatusTriagem.Falhou)
            {
                triagem.Falhar("A IA retornou uma resposta fora do formato esperado.", _execucao, _inicio);
            }
            else if (s != StatusTriagem.Pendente)
            {
                triagem.Concluir(_sugestao, _execucao, _inicio);
                if (s == StatusTriagem.Aceita)
                {
                    triagem.Aceitar(chamado, "Ana", _inicio);
                }
            }
        }

        if (finalizar)
        {
            chamado.MudarStatus(StatusChamado.Cancelado, "Ana", null, _inicio.AddHours(1));
        }

        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Chamados.Add(chamado);
        if (triagem is not null)
        {
            db.Triagens.Add(triagem);
        }

        await db.SaveChangesAsync(Ct);
        return chamado.Id;
    }

    private async Task<HttpResponseMessage> DecidirAsync(Guid id, string acao, object corpo, string? ifMatch = null)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/api/chamados/{id}/triagem/{acao}")
        {
            Content = JsonContent.Create(corpo),
        };
        if (ifMatch is not null)
        {
            requisicao.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await api.CreateClient().SendAsync(requisicao, Ct);
    }

    private async Task<string> ETagAsync(Guid id)
    {
        using var resposta = await api.CreateClient().GetAsync($"/api/chamados/{id}", Ct);
        return resposta.Headers.ETag!.ToString();
    }

    private async Task<TriagemIA> LerTriagemAsync(Guid chamadoId)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>()
            .Triagens.Where(t => t.ChamadoId == chamadoId).OrderByDescending(t => t.CriadoEm).FirstAsync(Ct);
    }

    private static async Task<JsonElement> LerAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return json.RootElement.Clone();
    }
}
