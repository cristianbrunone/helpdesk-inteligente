using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDesk.Application.Chamados;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia.Seed;
using HelpDesk.IntegrationTests.Infraestrutura;

namespace HelpDesk.IntegrationTests.Api;

/// <summary>
/// Regras de acesso do solicitante (ADR-0026, contrato §3):
/// - O solicitante só vê seus próprios chamados na listagem.
/// - O detalhe do próprio chamado vem sem triagem (triagem = null) e com transicoesPermitidas vazias.
/// - Tentar acessar ou comentar no chamado de outro solicitante dá 404 (sem vazar a existência).
/// - Atendente continua acessando tudo normalmente.
/// </summary>
public sealed class SolicitanteChamadosTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listar_Solicitante_RetornaApenasOsPropriosChamados()
    {
        var clienteMarina = api.CriarCliente(GeradorSeedUsuarios.MarinaSolicitante);
        var clientePaulo = api.CriarCliente(GeradorSeedUsuarios.PauloSolicitante);
        var clienteAtendente = api.CriarClienteAtendente();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var chamadoMarinaId = await CriarChamadoAsync(clienteMarina, $"Chamado Marina {sufixo}");
        var chamadoPauloId = await CriarChamadoAsync(clientePaulo, $"Chamado Paulo {sufixo}");

        // Marina lista: vê o seu chamado, mas não o do Paulo
        using var respMarina = await clienteMarina.GetAsync($"/api/chamados?q={sufixo}", Ct);
        respMarina.StatusCode.ShouldBe(HttpStatusCode.OK);
        var itensMarina = await ItensAsync(respMarina);
        itensMarina.ShouldContain(i => i.GetProperty("id").GetString() == chamadoMarinaId.ToString());
        itensMarina.ShouldNotContain(i => i.GetProperty("id").GetString() == chamadoPauloId.ToString());

        // Paulo lista: vê o seu chamado, mas não o da Marina
        using var respPaulo = await clientePaulo.GetAsync($"/api/chamados?q={sufixo}", Ct);
        respPaulo.StatusCode.ShouldBe(HttpStatusCode.OK);
        var itensPaulo = await ItensAsync(respPaulo);
        itensPaulo.ShouldContain(i => i.GetProperty("id").GetString() == chamadoPauloId.ToString());
        itensPaulo.ShouldNotContain(i => i.GetProperty("id").GetString() == chamadoMarinaId.ToString());

        // Atendente lista: vê ambos
        using var respAtendente = await clienteAtendente.GetAsync($"/api/chamados?q={sufixo}", Ct);
        respAtendente.StatusCode.ShouldBe(HttpStatusCode.OK);
        var itensAtendente = await ItensAsync(respAtendente);
        itensAtendente.ShouldContain(i => i.GetProperty("id").GetString() == chamadoMarinaId.ToString());
        itensAtendente.ShouldContain(i => i.GetProperty("id").GetString() == chamadoPauloId.ToString());
    }

    [Fact]
    public async Task Obter_SolicitanteNoProprioChamado_RetornaSemTriagemESemTransicoes()
    {
        var clienteMarina = api.CriarCliente(GeradorSeedUsuarios.MarinaSolicitante);
        var chamadoId = await CriarChamadoAsync(clienteMarina, "Meu chamado próprio");

        using var resposta = await clienteMarina.GetAsync($"/api/chamados/{chamadoId}", Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        var raiz = json.RootElement;
        raiz.GetProperty("id").GetString().ShouldBe(chamadoId.ToString());
        raiz.GetProperty("triagem").ValueKind.ShouldBe(JsonValueKind.Null);
        raiz.GetProperty("transicoesPermitidas").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Obter_SolicitanteEmChamadoAlheio_Retorna404NaoEncontrado()
    {
        var clienteMarina = api.CriarCliente(GeradorSeedUsuarios.MarinaSolicitante);
        var clientePaulo = api.CriarCliente(GeradorSeedUsuarios.PauloSolicitante);

        var chamadoPauloId = await CriarChamadoAsync(clientePaulo, "Chamado exclusivo do Paulo");

        // Marina tenta ver o chamado do Paulo: 404 (para não confirmar a existência)
        using var resposta = await clienteMarina.GetAsync($"/api/chamados/{chamadoPauloId}", Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("nao_encontrado");
    }

    [Fact]
    public async Task Comentar_SolicitanteEmChamadoAlheio_Retorna404NaoEncontrado()
    {
        var clienteMarina = api.CriarCliente(GeradorSeedUsuarios.MarinaSolicitante);
        var clientePaulo = api.CriarCliente(GeradorSeedUsuarios.PauloSolicitante);

        var chamadoPauloId = await CriarChamadoAsync(clientePaulo, "Chamado exclusivo do Paulo");

        using var resposta = await clienteMarina.PostAsJsonAsync(
            $"/api/chamados/{chamadoPauloId}/comentarios",
            new NovoComentario("Marina", "Tentando comentar no chamado alheio"),
            Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Comentar_SolicitanteNoProprioChamado_PermiteComentar()
    {
        var clienteMarina = api.CriarCliente(GeradorSeedUsuarios.MarinaSolicitante);
        var chamadoId = await CriarChamadoAsync(clienteMarina, "Chamado da Marina para comentar");

        using var resposta = await clienteMarina.PostAsJsonAsync(
            $"/api/chamados/{chamadoId}/comentarios",
            new NovoComentario("Marina", "Comentário legítimo no próprio chamado"),
            Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<Guid> CriarChamadoAsync(HttpClient cliente, string titulo)
    {
        var corpo = new
        {
            titulo,
            descricao = "Descrição com mais de dez caracteres.",
            solicitanteNome = "Solicitante Teste",
            solicitanteEmail = "teste@example.com",
            prioridade = "Media",
        };
        using var resposta = await cliente.PostAsJsonAsync("/api/chamados", corpo, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> ItensAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return [.. json.RootElement.GetProperty("itens").EnumerateArray().Select(i => i.Clone())];
    }
}
