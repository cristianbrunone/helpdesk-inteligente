using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Api;

public sealed class ComentariosTests(ApiFactory api, BancoFixture banco) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset _inicio = new(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(StatusChamado.Aberto)]
    [InlineData(StatusChamado.EmAndamento)]
    [InlineData(StatusChamado.Resolvido)]
    public async Task Comentar_ChamadoNaoFinalizado_Retorna201EApareceNoDetalhe(StatusChamado status)
    {
        var id = await CriarEmAsync(status);
        var etagAntes = await ETagAsync(id);

        using var resposta = await ComentarAsync(id, new { autor = "Ana (suporte)", texto = "  Pode me enviar um print?  " });

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        resposta.Headers.Location!.ToString().ShouldBe($"/api/chamados/{id}");
        resposta.Headers.ETag.ShouldNotBeNull();
        resposta.Headers.ETag.ToString().ShouldNotBe(etagAntes);
        var comentario = await LerAsync(resposta);
        comentario.EnumerateObject().Select(p => p.Name).ShouldBe(["id", "autor", "texto", "criadoEm"]);
        comentario.GetProperty("texto").GetString().ShouldBe("Pode me enviar um print?");

        using var detalhe = await api.CreateClient().GetAsync($"/api/chamados/{id}", Ct);
        detalhe.Headers.ETag.ShouldBe(resposta.Headers.ETag);
        (await LerAsync(detalhe)).GetProperty("comentarios").EnumerateArray()
            .ShouldContain(c => c.GetProperty("id").GetGuid() == comentario.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData(StatusChamado.Fechado)]
    [InlineData(StatusChamado.Cancelado)]
    public async Task Comentar_ChamadoFinalizado_Retorna409ChamadoFinalizado(StatusChamado status)
    {
        var id = await CriarEmAsync(status);

        using var resposta = await ComentarAsync(id, new { autor = "Ana", texto = "Olá" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("chamado_finalizado");
    }

    [Fact]
    public async Task Comentar_TextoVazioELongoDemais_Retorna422PorCampo()
    {
        var id = await CriarEmAsync(StatusChamado.Aberto);

        using var vazio = await ComentarAsync(id, new { autor = "Ana", texto = " " });
        using var longo = await ComentarAsync(id, new { autor = "", texto = new string('x', 4001) });

        vazio.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        Campos(await LerAsync(vazio)).ShouldBe(["texto"]);
        longo.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        Campos(await LerAsync(longo)).ShouldBe(["autor", "texto"], ignoreOrder: true);
    }

    [Fact]
    public async Task Comentar_IfMatchDesatualizado_Retorna412()
    {
        var id = await CriarEmAsync(StatusChamado.Aberto);
        var etagLido = await ETagAsync(id);
        using (var primeiro = await ComentarAsync(id, new { autor = "Bruno", texto = "Primeiro" }, etagLido))
        {
            primeiro.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var resposta = await ComentarAsync(id, new { autor = "Ana", texto = "Segundo" }, etagLido);

        resposta.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("versao_desatualizada");
    }

    [Fact]
    public async Task Comentar_ChamadoInexistente_Retorna404()
    {
        using var resposta = await ComentarAsync(Guid.CreateVersion7(), new { autor = "Ana", texto = "Olá" });

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- Apoio ----------

    private async Task<Guid> CriarEmAsync(StatusChamado status)
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo", "maria@example.com", null, null, _inicio);
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

    private async Task<HttpResponseMessage> ComentarAsync(Guid id, object corpo, string? ifMatch = null)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/api/chamados/{id}/comentarios")
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

    private static List<string> Campos(JsonElement problema) =>
        [.. problema.GetProperty("errors").EnumerateObject().Select(p => p.Name)];

    private static async Task<JsonElement> LerAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return json.RootElement.Clone();
    }
}
