using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Api;

public sealed class ObterChamadoTests(ApiFactory api, BancoFixture banco) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset _inicio = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Obter_ChamadoRecemCriado_RetornaOMesmoDetalheEETagDoPost()
    {
        var cliente = api.CriarClienteAtendente();
        using var criacao = await cliente.PostAsJsonAsync("/api/chamados", new
        {
            titulo = "Impressora não imprime",
            descricao = "A fila mostra o documento, mas nada sai.",
            solicitanteNome = "Maria Exemplo",
            solicitanteEmail = "maria.obter@example.com",
        }, Ct);
        var criado = await LerAsync(criacao);

        using var resposta = await cliente.GetAsync(criacao.Headers.Location, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        resposta.Headers.ETag.ShouldBe(criacao.Headers.ETag);
        var detalhe = await LerAsync(resposta);
        detalhe.GetRawText().ShouldBe(criado.GetRawText());
    }

    [Fact]
    public async Task Obter_ChamadoResolvido_TrazComentariosEHistoricoEmOrdemETransicoesDoDominio()
    {
        var chamado = NovoChamado();
        chamado.Comentar("Ana (suporte)", "Pode me enviar um print?", _inicio.AddMinutes(30));
        chamado.MudarStatus(StatusChamado.EmAndamento, "Ana (suporte)", null, _inicio.AddHours(1));
        chamado.Comentar("Maria Exemplo", "Segue o print.", _inicio.AddHours(2));
        chamado.MudarStatus(StatusChamado.Resolvido, "Ana (suporte)", "Permissão reaplicada.", _inicio.AddHours(3));
        await SalvarAsync(chamado);

        var detalhe = await ObterAsync(chamado.Id);

        detalhe.GetProperty("status").GetString().ShouldBe("Resolvido");
        detalhe.GetProperty("resolvidoEm").GetDateTimeOffset().ShouldBe(_inicio.AddHours(3));
        detalhe.GetProperty("podeComentar").GetBoolean().ShouldBeTrue();
        Textos(detalhe.GetProperty("transicoesPermitidas")).ShouldBe(["Fechado", "EmAndamento"]);
        detalhe.GetProperty("comentarios").EnumerateArray().Select(c => c.GetProperty("texto").GetString())
            .ShouldBe(["Pode me enviar um print?", "Segue o print.", "Permissão reaplicada."]);

        var historico = detalhe.GetProperty("historico").EnumerateArray().ToList();
        historico.Select(h => h.GetProperty("statusNovo").GetString()).ShouldBe(["Aberto", "EmAndamento", "Resolvido"]);
        historico[1].GetProperty("statusAnterior").GetString().ShouldBe("Aberto");
        historico[1].GetProperty("alteradoPor").GetString().ShouldBe("Ana (suporte)");
    }

    [Fact]
    public async Task Obter_ChamadoFechado_NaoOfereceTransicoesNemComentario()
    {
        var chamado = NovoChamado();
        chamado.MudarStatus(StatusChamado.EmAndamento, "Ana", null, _inicio.AddHours(1));
        chamado.MudarStatus(StatusChamado.Resolvido, "Ana", null, _inicio.AddHours(2));
        chamado.MudarStatus(StatusChamado.Fechado, "Ana", null, _inicio.AddDays(2));
        await SalvarAsync(chamado);

        var detalhe = await ObterAsync(chamado.Id);

        detalhe.GetProperty("transicoesPermitidas").GetArrayLength().ShouldBe(0);
        detalhe.GetProperty("podeComentar").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Obter_DepoisDeUmaAlteracao_DevolveOutroETag()
    {
        var chamado = NovoChamado();
        await SalvarAsync(chamado);
        using var antes = await api.CriarClienteAtendente().GetAsync($"/api/chamados/{chamado.Id}", Ct);

        await using (var servicos = banco.CriarServicos())
        await using (var escopo = servicos.CreateAsyncScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            var salvo = await db.Chamados.Include(c => c.Historico).SingleAsync(c => c.Id == chamado.Id, Ct);
            salvo.MudarStatus(StatusChamado.EmAndamento, "Ana", null, _inicio.AddHours(1));
            await db.SaveChangesAsync(Ct);
        }

        using var depois = await api.CriarClienteAtendente().GetAsync($"/api/chamados/{chamado.Id}", Ct);

        antes.Headers.ETag.ShouldNotBeNull();
        depois.Headers.ETag.ShouldNotBeNull();
        depois.Headers.ETag.ShouldNotBe(antes.Headers.ETag);
    }

    [Fact]
    public async Task Obter_IdInexistente_Retorna404NaoEncontrado()
    {
        var id = Guid.CreateVersion7();

        using var resposta = await api.CriarClienteAtendente().GetAsync($"/api/chamados/{id}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problema = await LerAsync(resposta);
        problema.GetProperty("codigo").GetString().ShouldBe("nao_encontrado");
        problema.GetProperty("detail").GetString().ShouldBe($"O chamado '{id}' não foi encontrado.");
        problema.GetProperty("instance").GetString().ShouldBe($"/api/chamados/{id}");
    }

    [Fact]
    public async Task Obter_IdQueNaoEGuid_Retorna404NaoEncontrado()
    {
        using var resposta = await api.CriarClienteAtendente().GetAsync("/api/chamados/abc", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await LerAsync(resposta)).GetProperty("codigo").GetString().ShouldBe("nao_encontrado");
    }

    // ---------- Apoio ----------

    private static Chamado NovoChamado() => Chamado.Abrir(
        "Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
        "Maria Exemplo", "maria@example.com", 2, Prioridade.Alta, _inicio);

    private async Task SalvarAsync(Chamado chamado)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Chamados.Add(chamado);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<JsonElement> ObterAsync(Guid id)
    {
        using var resposta = await api.CriarClienteAtendente().GetAsync($"/api/chamados/{id}", Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await LerAsync(resposta);
    }

    private static async Task<JsonElement> LerAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return json.RootElement.Clone();
    }

    private static List<string?> Textos(JsonElement lista) => [.. lista.EnumerateArray().Select(t => t.GetString())];
}
