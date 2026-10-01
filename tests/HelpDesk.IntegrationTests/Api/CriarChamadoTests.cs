using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HelpDesk.IntegrationTests.Infraestrutura;

namespace HelpDesk.IntegrationTests.Api;

public sealed class CriarChamadoTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object CorpoValido(object? categoriaId = null, string? prioridade = null) => new
    {
        titulo = "Não consigo acessar o portal financeiro",
        descricao = "Desde ontem aparece erro 403 ao abrir o módulo de boletos.",
        solicitanteNome = "Maria Exemplo",
        solicitanteEmail = "maria.criar@example.com",
        categoriaId,
        prioridade,
    };

    [Fact]
    public async Task Criar_DadosValidos_Retorna201ComLocationETagEDetalheDoContrato()
    {
        using var resposta = await api.CreateClient().PostAsJsonAsync("/api/chamados", CorpoValido(), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        resposta.Headers.ETag.ShouldNotBeNull();
        resposta.Headers.ETag.IsWeak.ShouldBeFalse();
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        var chamado = json.RootElement;
        var id = chamado.GetProperty("id").GetGuid();
        resposta.Headers.Location!.ToString().ShouldBe($"/api/chamados/{id}");

        chamado.GetProperty("numero").GetInt64().ShouldBeGreaterThan(0);
        chamado.GetProperty("status").GetString().ShouldBe("Aberto");
        chamado.GetProperty("prioridade").GetString().ShouldBe("Media");
        chamado.GetProperty("categoria").ValueKind.ShouldBe(JsonValueKind.Null);
        chamado.GetProperty("resolvidoEm").ValueKind.ShouldBe(JsonValueKind.Null);
        chamado.GetProperty("solicitanteEmail").GetString().ShouldBe("maria.criar@example.com");
        chamado.GetProperty("podeComentar").GetBoolean().ShouldBeTrue();
        chamado.GetProperty("transicoesPermitidas").EnumerateArray().Select(t => t.GetString())
            .ShouldBe(["EmAndamento", "Cancelado"]);
        chamado.GetProperty("comentarios").GetArrayLength().ShouldBe(0);

        var historico = chamado.GetProperty("historico").EnumerateArray().ShouldHaveSingleItem();
        historico.GetProperty("statusAnterior").ValueKind.ShouldBe(JsonValueKind.Null);
        historico.GetProperty("statusNovo").GetString().ShouldBe("Aberto");
        historico.GetProperty("alteradoPor").GetString().ShouldBe("sistema");
    }

    [Fact]
    public async Task Criar_ComCategoriaEPrioridade_RetornaCategoriaComNomeEPrioridadeInformada()
    {
        using var resposta = await api.CreateClient()
            .PostAsJsonAsync("/api/chamados", CorpoValido(categoriaId: 2, prioridade: "Critica"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        var chamado = json.RootElement;
        chamado.GetProperty("categoria").GetProperty("id").GetInt16().ShouldBe((short)2);
        chamado.GetProperty("categoria").GetProperty("nome").GetString().ShouldBe("Financeiro");
        chamado.GetProperty("prioridade").GetString().ShouldBe("Critica");
        // RN-05 já refletida nos botões: Crítico aberto não oferece "Cancelado".
        chamado.GetProperty("transicoesPermitidas").EnumerateArray().Select(t => t.GetString())
            .ShouldBe(["EmAndamento"]);
    }

    [Fact]
    public async Task Criar_CamposInvalidos_Retorna422ComTodosOsErrosPorCampoEmCamelCase()
    {
        var corpo = new { titulo = "abc", descricao = "", solicitanteNome = (string?)null, solicitanteEmail = "x@y", categoriaId = 999 };

        using var resposta = await api.CreateClient().PostAsJsonAsync("/api/chamados", corpo, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        resposta.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        var problema = json.RootElement;
        problema.GetProperty("codigo").GetString().ShouldBe("validacao");
        problema.GetProperty("type").GetString().ShouldBe("https://helpdesk.local/problemas/validacao");
        problema.GetProperty("instance").GetString().ShouldBe("/api/chamados");
        problema.TryGetProperty("correlationId", out _).ShouldBeTrue();

        var erros = problema.GetProperty("errors");
        erros.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["titulo", "descricao", "solicitanteNome", "solicitanteEmail", "categoriaId"], ignoreOrder: true);
        erros.GetProperty("titulo")[0].GetString().ShouldBe("O título deve ter entre 5 e 150 caracteres.");
        erros.GetProperty("categoriaId")[0].GetString().ShouldBe("A categoria informada não existe.");
    }

    [Fact]
    public async Task Criar_SoCategoriaInexistente_Retorna422NoCampoCategoriaId()
    {
        using var resposta = await api.CreateClient()
            .PostAsJsonAsync("/api/chamados", CorpoValido(categoriaId: 999), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["categoriaId"]);
    }

    [Theory]
    [InlineData("{ \"titulo\": ")]
    [InlineData("{ \"titulo\": \"Erro no boleto\", \"prioridade\": \"Urgente\" }")]
    [InlineData("{ \"titulo\": 123 }")]
    [InlineData("{ \"titulo\": \"Erro no boleto\", \"prioridade\": 2 }")]
    public async Task Criar_JsonMalformadoOuTipoErrado_Retorna400(string corpo)
    {
        using var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");

        using var resposta = await api.CreateClient().PostAsync("/api/chamados", conteudo, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("requisicao_invalida");
    }

    [Fact]
    public async Task Criar_ChamadoComDadosPessoais_NaoRegistraNomeNemEmailNosLogs()
    {
        var corpo = new
        {
            titulo = "Erro ao emitir nota",
            descricao = "A nota não sai. Meu CPF é 987.654.321-00.",
            solicitanteNome = "Pessoa Rastreavel Logs",
            solicitanteEmail = "pessoa.rastreavel@example.com",
        };

        using var resposta = await api.CreateClient().PostAsJsonAsync("/api/chamados", corpo, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var mensagens = api.Logs.Registros.Select(r => r.Mensagem).ToList();
        mensagens.ShouldNotBeEmpty();
        mensagens.ShouldAllBe(m => !m.Contains("pessoa.rastreavel") && !m.Contains("Rastreavel")
            && !m.Contains("987.654.321-00"));
    }
}
