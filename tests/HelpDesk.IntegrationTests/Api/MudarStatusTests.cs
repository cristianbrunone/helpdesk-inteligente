using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Api;

public sealed class MudarStatusTests(ApiFactory api, BancoFixture banco) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset _inicio = new(2026, 4, 6, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Transições permitidas (RN-01, RN-02, RN-03) ----------

    [Theory]
    [InlineData(StatusChamado.Aberto, "EmAndamento")]
    [InlineData(StatusChamado.Aberto, "Cancelado")]
    [InlineData(StatusChamado.EmAndamento, "Resolvido")]
    [InlineData(StatusChamado.Resolvido, "Fechado")]
    [InlineData(StatusChamado.Resolvido, "EmAndamento")]
    public async Task Patch_TransicaoPermitida_Retorna200ComNovoStatusHistoricoENovoETag(
        StatusChamado origem, string destino)
    {
        var id = await CriarEmAsync(origem);
        var etagAntes = await ETagAsync(id);

        using var resposta = await PatchAsync(id, new { status = destino });

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        resposta.Headers.ETag.ShouldNotBeNull();
        resposta.Headers.ETag.ToString().ShouldNotBe(etagAntes);
        var detalhe = await LerAsync(resposta);
        detalhe.GetProperty("status").GetString().ShouldBe(destino);
        var ultimo = detalhe.GetProperty("historico").EnumerateArray().Last();
        ultimo.GetProperty("statusAnterior").GetString().ShouldBe(origem.ToString());
        ultimo.GetProperty("statusNovo").GetString().ShouldBe(destino);
        ultimo.GetProperty("alteradoPor").GetString().ShouldBe("Ana (suporte)");
    }

    [Fact]
    public async Task Patch_ResolverEReabrir_PreencheELimpaResolvidoEm()
    {
        var id = await CriarEmAsync(StatusChamado.EmAndamento);

        using var resolvido = await PatchAsync(id, new { status = "Resolvido" });
        using var reaberto = await PatchAsync(id, new { status = "EmAndamento" });

        (await LerAsync(resolvido)).GetProperty("resolvidoEm").ValueKind.ShouldBe(JsonValueKind.String);
        (await LerAsync(reaberto)).GetProperty("resolvidoEm").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Patch_ComComentario_GravaComentarioNaMesmaOperacao()
    {
        var id = await CriarEmAsync(StatusChamado.EmAndamento);

        using var resposta = await PatchAsync(id,
            new { status = "Resolvido", comentario = "Permissão reaplicada no perfil." });

        var detalhe = await LerAsync(resposta);
        var comentario = detalhe.GetProperty("comentarios").EnumerateArray().ShouldHaveSingleItem();
        comentario.GetProperty("texto").GetString().ShouldBe("Permissão reaplicada no perfil.");
        comentario.GetProperty("autor").GetString().ShouldBe("Ana (suporte)");
        comentario.GetProperty("criadoEm").GetDateTimeOffset()
            .ShouldBe(detalhe.GetProperty("historico").EnumerateArray().Last().GetProperty("alteradoEm").GetDateTimeOffset());
    }

    // ---------- Conflitos (409) ----------

    [Fact]
    public async Task Patch_TransicaoProibida_Retorna409ComTransicoesPermitidasENaoGravaNada()
    {
        var id = await CriarEmAsync(StatusChamado.Aberto);

        using var resposta = await PatchAsync(id,
            new { status = "Resolvido", comentario = "Não deveria ser gravado." });

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problema = await LerAsync(resposta);
        problema.GetProperty("codigo").GetString().ShouldBe("transicao_invalida");
        problema.GetProperty("detail").GetString().ShouldBe("Não é possível ir de 'Aberto' para 'Resolvido'.");
        problema.GetProperty("transicoesPermitidas").EnumerateArray().Select(t => t.GetString())
            .ShouldBe(["EmAndamento", "Cancelado"]);

        var detalhe = await ObterAsync(id);
        detalhe.GetProperty("status").GetString().ShouldBe("Aberto");
        detalhe.GetProperty("comentarios").GetArrayLength().ShouldBe(0);
        detalhe.GetProperty("historico").GetArrayLength().ShouldBe(1);
    }

    [Theory]
    [InlineData(StatusChamado.Fechado)]
    [InlineData(StatusChamado.Cancelado)]
    public async Task Patch_ChamadoFinalizado_Retorna409ChamadoFinalizado(StatusChamado finalizado)
    {
        var id = await CriarEmAsync(finalizado);

        using var resposta = await PatchAsync(id, new { status = "EmAndamento" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("chamado_finalizado");
    }

    [Fact]
    public async Task Patch_CriticoParaCancelado_Retorna409CriticoNaoCancelavel()
    {
        var id = await CriarEmAsync(StatusChamado.Aberto, Prioridade.Critica);

        using var resposta = await PatchAsync(id, new { status = "Cancelado" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("critico_nao_cancelavel");
    }

    // ---------- Concorrência (412) ----------

    [Fact]
    public async Task Patch_IfMatchDesatualizado_Retorna412ENaoAltera()
    {
        var id = await CriarEmAsync(StatusChamado.Aberto);
        var etagLido = await ETagAsync(id);
        // Outro atendente muda o chamado depois da leitura.
        using (var outro = await PatchAsync(id, new { status = "EmAndamento" }, etagLido))
        {
            outro.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var resposta = await PatchAsync(id, new { status = "Resolvido" }, etagLido);

        resposta.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var problema = await LerAsync(resposta);
        problema.GetProperty("codigo").GetString().ShouldBe("versao_desatualizada");
        problema.GetProperty("type").GetString().ShouldBe("https://helpdesk.local/problemas/versao-desatualizada");
        (await ObterAsync(id)).GetProperty("status").GetString().ShouldBe("EmAndamento");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("atual")]
    public async Task Patch_IfMatchQueConfere_Retorna200(string ifMatch)
    {
        var id = await CriarEmAsync(StatusChamado.Aberto);
        var valor = ifMatch == "atual" ? await ETagAsync(id) : ifMatch;

        using var resposta = await PatchAsync(id, new { status = "EmAndamento" }, valor);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Patch_IfMatchFracoOuMalformado_Retorna412()
    {
        var id = await CriarEmAsync(StatusChamado.Aberto);
        var atual = await ETagAsync(id);

        using var fraco = await PatchAsync(id, new { status = "EmAndamento" }, $"W/{atual}");
        using var malformado = await PatchAsync(id, new { status = "EmAndamento" }, "sem-aspas");

        fraco.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        malformado.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    // ---------- Validação e requisição ----------

    [Fact]
    public async Task Patch_SemAlteradoPorNoCorpo_UsaIdentidadeDoUsuarioAutenticado()
    {
        var id = await CriarEmAsync(StatusChamado.Aberto);

        using var resposta = await PatchAsync(id, new { status = "EmAndamento" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detalhe = await LerAsync(resposta);
        detalhe.GetProperty("historico").EnumerateArray().Last().GetProperty("alteradoPor").GetString()
            .ShouldBe("Ana (suporte)");
    }

    [Fact]
    public async Task Patch_SemStatus_Retorna422NoCampoStatus()
    {
        var id = await CriarEmAsync(StatusChamado.Aberto);

        using var resposta = await PatchAsync(id, new { comentario = "Sem status" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await LerAsync(resposta)).GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["status"]);
    }

    [Theory]
    [InlineData("{ \"status\": \"Voando\" }")]
    [InlineData("{ \"status\": 1 }")]
    public async Task Patch_StatusForaDoContrato_Retorna400(string corpo)
    {
        var id = await CriarEmAsync(StatusChamado.Aberto);
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");

        using var resposta = await api.CriarClienteAtendente().PatchAsync($"/api/chamados/{id}/status", conteudo, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_ChamadoInexistente_Retorna404()
    {
        using var resposta = await PatchAsync(Guid.CreateVersion7(), new { status = "EmAndamento" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- Apoio ----------

    private async Task<Guid> CriarEmAsync(StatusChamado status, Prioridade prioridade = Prioridade.Media)
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo", "maria@example.com", null, prioridade, _inicio);
        StatusChamado[] caminho = status switch
        {
            StatusChamado.EmAndamento => [StatusChamado.EmAndamento],
            StatusChamado.Resolvido => [StatusChamado.EmAndamento, StatusChamado.Resolvido],
            StatusChamado.Fechado => [StatusChamado.EmAndamento, StatusChamado.Resolvido, StatusChamado.Fechado],
            StatusChamado.Cancelado => [StatusChamado.Cancelado],
            _ => [],
        };
        var quando = _inicio;
        foreach (var passo in caminho)
        {
            quando = quando.AddHours(1);
            chamado.MudarStatus(passo, "Ana", null, quando);
        }

        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Chamados.Add(chamado);
        await db.SaveChangesAsync(Ct);
        return chamado.Id;
    }

    private async Task<HttpResponseMessage> PatchAsync(Guid id, object corpo, string? ifMatch = null)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Patch, $"/api/chamados/{id}/status")
        {
            Content = JsonContent.Create(corpo),
        };
        if (ifMatch is not null)
        {
            requisicao.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await api.CriarClienteAtendente().SendAsync(requisicao, Ct);
    }

    private async Task<string> ETagAsync(Guid id)
    {
        using var resposta = await api.CriarClienteAtendente().GetAsync($"/api/chamados/{id}", Ct);
        return resposta.Headers.ETag!.ToString();
    }

    private async Task<JsonElement> ObterAsync(Guid id)
    {
        using var resposta = await api.CriarClienteAtendente().GetAsync($"/api/chamados/{id}", Ct);
        return await LerAsync(resposta);
    }

    private static async Task<JsonElement> LerAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return json.RootElement.Clone();
    }
}
