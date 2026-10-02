using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.AspNetCore.Hosting;

namespace HelpDesk.IntegrationTests.Copiloto;

/// <summary>
/// O copiloto exposto via SSE na API (ADR-0012, contrato §copiloto). Testa a sequência de eventos, o rate limit,
/// o guardrail de saída com CPF mascarado e aviso de citação não verificada, o kill switch (503), e o cancelamento.
/// </summary>
public sealed class CopilotoEndpointsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- Sequência SSE ----------

    [Fact]
    public async Task Conversar_JaTivemosCasosParecidos_SequenciaCompletaDeEventosSse()
    {
        var chamadoId = await CriarChamadoAsync("Erro 403 ao emitir boletos");
        var cliente = api.CreateClient();

        using var resposta = await cliente.PostAsJsonAsync($"/api/chamados/{chamadoId}/copiloto",
            Corpo([Pergunta("Já tivemos casos parecidos?")]), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        resposta.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        resposta.Headers.Contains("X-Correlation-Id").ShouldBeTrue();

        var eventos = await LerEventosSseAsync(resposta, Ct);
        eventos.Select(e => e.Tipo).ShouldBe(
            ["ferramenta", "ferramenta", "delta", "fontes", "fim"], ignoreOrder: false);

        var inicioFerramenta = JsonDocument.Parse(eventos[0].Dados).RootElement;
        inicioFerramenta.GetProperty("nome").GetString().ShouldBe("buscar_chamados_similares");
        inicioFerramenta.GetProperty("fase").GetString().ShouldBe("iniciada");
        inicioFerramenta.GetProperty("descricao").GetString().ShouldBe("Buscando chamados semelhantes resolvidos");

        var fimFerramenta = JsonDocument.Parse(eventos[1].Dados).RootElement;
        fimFerramenta.GetProperty("nome").GetString().ShouldBe("buscar_chamados_similares");
        fimFerramenta.GetProperty("fase").GetString().ShouldBe("concluida");
        fimFerramenta.GetProperty("resultados").GetInt32().ShouldBeGreaterThanOrEqualTo(0);

        var delta = JsonDocument.Parse(eventos[2].Dados).RootElement;
        delta.GetProperty("texto").GetString().ShouldNotBeNullOrWhiteSpace();

        var fontes = JsonDocument.Parse(eventos[3].Dados).RootElement;
        fontes.GetProperty("itens").ValueKind.ShouldBe(JsonValueKind.Array);

        var fim = JsonDocument.Parse(eventos[4].Dados).RootElement;
        fim.GetProperty("tokensEntrada").GetInt64().ShouldBeGreaterThan(0);
        fim.GetProperty("tokensSaida").GetInt64().ShouldBeGreaterThan(0);
    }

    // ---------- Chamado em contexto ----------

    [Fact]
    public async Task Conversar_FerramentaDeHistorico_RespeitaOChamadoDaRota()
    {
        var idA = await CriarChamadoAsync("Chamado A para histórico");
        var cliente = api.CreateClient();

        using var resposta = await cliente.PostAsJsonAsync($"/api/chamados/{idA}/copiloto",
            Corpo([Pergunta("Qual é o histórico deste chamado?")]), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eventos = await LerEventosSseAsync(resposta, Ct);

        var ferramentas = eventos.Where(e => e.Tipo == "ferramenta").ToList();
        ferramentas.Count.ShouldBeGreaterThanOrEqualTo(2);
        var primeira = JsonDocument.Parse(ferramentas[0].Dados).RootElement;
        primeira.GetProperty("nome").GetString().ShouldBe("obter_historico_do_chamado");
    }

    // ---------- Rate Limiter (429) ----------

    [Fact]
    public async Task Conversar_AtingeLimitePorMinuto_Retorna429ComProblemDetailsERetryAfter()
    {
        await using var comLimite = api.WithWebHostBuilder(b =>
            b.UseSetting("COPILOTO_RATE_LIMIT_POR_MINUTO", "2"));
        var cliente = comLimite.CreateClient();
        var chamadoId = await CriarChamadoAsync("Chamado para teste de rate limit");

        // 2 requisições dentro da cota
        for (var i = 0; i < 2; i++)
        {
            using var ok = await cliente.PostAsJsonAsync($"/api/chamados/{chamadoId}/copiloto",
                Corpo([Pergunta("Pergunta dentro do limite")]), Ct);
            ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // 3ª requisição estoura o limite
        using var bloqueada = await cliente.PostAsJsonAsync($"/api/chamados/{chamadoId}/copiloto",
            Corpo([Pergunta("Pergunta excedente")]), Ct);

        bloqueada.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        bloqueada.Headers.Contains("Retry-After").ShouldBeTrue();
        using var json = JsonDocument.Parse(await bloqueada.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("limite_excedido");
    }

    // ---------- Guardrail: CPF mascarado + Aviso ----------

    [Fact]
    public async Task Conversar_ModoVazaDados_EmiteDeltaMascaradoEAvisoDeCitacaoInventada()
    {
        await using var fakeVaza = api.WithWebHostBuilder(b =>
            b.UseSetting("LLM_FAKE_MODO", "vaza_dados"));
        var cliente = fakeVaza.CreateClient();
        var chamadoId = await CriarChamadoAsync("Chamado para teste de guardrail");

        using var resposta = await cliente.PostAsJsonAsync($"/api/chamados/{chamadoId}/copiloto",
            Corpo([Pergunta("Já tivemos casos parecidos?")]), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var eventos = await LerEventosSseAsync(resposta, Ct);

        // O CPF cru nunca deve constar em nenhum delta
        var deltas = eventos.Where(e => e.Tipo == "delta")
            .Select(e => JsonDocument.Parse(e.Dados).RootElement.GetProperty("texto").GetString()!)
            .ToList();
        string.Concat(deltas).ShouldContain("[CPF]");
        deltas.ShouldAllBe(d => !d.Contains("529.982.247-25"));

        // O evento aviso deve alertar a citação inventada
        var aviso = eventos.Single(e => e.Tipo == "aviso");
        using var jsonAviso = JsonDocument.Parse(aviso.Dados);
        jsonAviso.RootElement.GetProperty("tipo").GetString().ShouldBe("referencia_nao_verificada");
        var refs = jsonAviso.RootElement.GetProperty("referencias").EnumerateArray()
            .Select(r => r.GetString()).ToList();
        refs.ShouldContain($"#{HelpDesk.Infrastructure.Ia.Fake.FakeCopiloto.NumeroInventado}");
    }

    // ---------- Kill switch (503) ----------

    [Fact]
    public async Task Conversar_CopilotoDesativado_Retorna503IaIndisponivel()
    {
        await using var semCopiloto = api.WithWebHostBuilder(b =>
            b.UseSetting("IA_COPILOTO_HABILITADO", "false"));
        var cliente = semCopiloto.CreateClient();
        var chamadoId = await CriarChamadoAsync("Chamado para teste de kill switch");

        using var resposta = await cliente.PostAsJsonAsync($"/api/chamados/{chamadoId}/copiloto",
            Corpo([Pergunta("Olá copiloto")]), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("ia_indisponivel");
        json.RootElement.GetProperty("detail").GetString()!.ShouldContain("desativado");
    }

    // ---------- Cancelamento ----------

    [Fact]
    public async Task Conversar_ClienteCancelaRequisicao_InterrompeSemErro500()
    {
        var chamadoId = await CriarChamadoAsync("Chamado para cancelamento");
        var cliente = api.CreateClient();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/chamados/{chamadoId}/copiloto")
        {
            Content = JsonContent.Create(Corpo([Pergunta("Já tivemos casos parecidos?")])),
        };

        using var resposta = await cliente.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var stream = await resposta.Content.ReadAsStreamAsync(cts.Token);
        using var leitor = new StreamReader(stream);

        // Lê a primeira linha e cancela
        var primeiraLinha = await leitor.ReadLineAsync(cts.Token);
        primeiraLinha.ShouldNotBeNull();
        await cts.CancelAsync();

        // Leitura subsequente após o cancelamento deve falhar com OperationCanceledException
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            while (await leitor.ReadLineAsync(cts.Token) is not null) { }
        });
    }

    // ---------- Validações HTTP padrão (404 / 422) ----------

    [Fact]
    public async Task Conversar_ChamadoInexistente_Retorna404RecursoNaoEncontrado()
    {
        var inexistente = Guid.CreateVersion7();
        using var resposta = await api.CreateClient().PostAsJsonAsync($"/api/chamados/{inexistente}/copiloto",
            Corpo([Pergunta("Olá")]), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("nao_encontrado");
    }

    [Fact]
    public async Task Conversar_MensagensVazias_Retorna422DadosInvalidos()
    {
        var chamadoId = await CriarChamadoAsync("Chamado para validação");
        using var resposta = await api.CreateClient().PostAsJsonAsync($"/api/chamados/{chamadoId}/copiloto",
            new { mensagens = Array.Empty<object>() }, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("validacao");
    }

    // ---------- Apoio ----------

    private static object Corpo(object[] mensagens) => new { mensagens };

    private static object Pergunta(string conteudo) => new { papel = "usuario", conteudo };

    private async Task<Guid> CriarChamadoAsync(string titulo)
    {
        using var resposta = await api.CreateClient().PostAsJsonAsync("/api/chamados", new
        {
            titulo,
            descricao = "Descrição detalhada do chamado para testes do copiloto.",
            solicitanteNome = "João Silva",
            solicitanteEmail = "joao.silva@example.com",
        }, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private sealed record EventoSse(string Tipo, string Dados);

    private static async Task<List<EventoSse>> LerEventosSseAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        var eventos = new List<EventoSse>();
        var stream = await resposta.Content.ReadAsStreamAsync(ct);
        using var leitor = new StreamReader(stream, Encoding.UTF8);

        string? tipoAtual = null;
        var dadosAtuais = new StringBuilder();

        while (await leitor.ReadLineAsync(ct) is { } linha)
        {
            if (linha.StartsWith("event: ", StringComparison.Ordinal))
            {
                tipoAtual = linha["event: ".Length..].Trim();
            }
            else if (linha.StartsWith("data: ", StringComparison.Ordinal))
            {
                if (dadosAtuais.Length > 0)
                {
                    dadosAtuais.Append('\n');
                }
                dadosAtuais.Append(linha["data: ".Length..]);
            }
            else if (string.IsNullOrWhiteSpace(linha))
            {
                if (tipoAtual is not null)
                {
                    eventos.Add(new EventoSse(tipoAtual, dadosAtuais.ToString()));
                    tipoAtual = null;
                    dadosAtuais.Clear();
                }
            }
        }

        if (tipoAtual is not null)
        {
            eventos.Add(new EventoSse(tipoAtual, dadosAtuais.ToString()));
        }

        return eventos;
    }
}
