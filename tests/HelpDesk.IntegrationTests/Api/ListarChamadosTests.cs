using System.Data.Common;
using System.Net;
using System.Text.Json;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Api;

/// <summary>
/// Cada teste cria seus chamados com um marcador único no título e filtra por ele (<c>q</c>), isolando-se do seed
/// e dos outros testes que rodam em paralelo no mesmo banco.
/// </summary>
public sealed class ListarChamadosTests(ApiFactory api, BancoFixture banco) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset _base = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Listar_SemParametros_UsaPadroesDoContratoESemEmail()
    {
        var pagina = await ListarAsync("");

        pagina.GetProperty("pagina").GetInt32().ShouldBe(1);
        pagina.GetProperty("tamanhoPagina").GetInt32().ShouldBe(20);
        pagina.GetProperty("totalItens").GetInt32().ShouldBeGreaterThanOrEqualTo(200); // seed
        var item = pagina.GetProperty("itens")[0];
        item.EnumerateObject().Select(p => p.Name).ShouldBe(
            ["id", "numero", "titulo", "status", "prioridade", "categoria", "solicitanteNome", "criadoEm", "atualizadoEm",
                "triagemStatus"]);
    }

    [Fact]
    public async Task Listar_FiltroDeStatusRepetido_RetornaOsDoisStatus()
    {
        var marcador = Marcador();
        await CriarAsync(marcador, status: StatusChamado.Aberto);
        await CriarAsync(marcador, status: StatusChamado.EmAndamento);
        await CriarAsync(marcador, status: StatusChamado.Cancelado);

        var status = Valores(await ListarAsync($"q={marcador}&status=Aberto&status=Cancelado"), "status");

        status.ShouldBe(["Aberto", "Cancelado"], ignoreOrder: true);
    }

    [Fact]
    public async Task Listar_FiltroDePrioridade_RetornaSoAsPrioridadesPedidas()
    {
        var marcador = Marcador();
        foreach (var prioridade in Enum.GetValues<Prioridade>())
        {
            await CriarAsync(marcador, prioridade: prioridade);
        }

        var prioridades = Valores(await ListarAsync($"q={marcador}&prioridade=Critica&prioridade=Baixa"), "prioridade");

        prioridades.ShouldBe(["Critica", "Baixa"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("categoriaId=1&categoriaId=3", new[] { "1", "3" })]
    [InlineData("semCategoria=true", new[] { "null" })]
    [InlineData("categoriaId=1&semCategoria=true", new[] { "1", "null" })]
    public async Task Listar_FiltroDeCategoria_CombinaCategoriasESemCategoria(string filtro, string[] esperadas)
    {
        var marcador = Marcador();
        await CriarAsync(marcador, categoriaId: 1);
        await CriarAsync(marcador, categoriaId: 3);
        await CriarAsync(marcador, categoriaId: 5);
        await CriarAsync(marcador, categoriaId: null);

        var itens = (await ListarAsync($"q={marcador}&{filtro}")).GetProperty("itens").EnumerateArray();
        var categorias = itens.Select(i => i.GetProperty("categoria") is { ValueKind: JsonValueKind.Object } c
            ? c.GetProperty("id").GetInt16().ToString() : "null");

        categorias.ShouldBe(esperadas, ignoreOrder: true);
    }

    [Fact]
    public async Task Listar_FiltroDePeriodo_UsaIntervaloFechadoAbertoEmUtc()
    {
        var marcador = Marcador();
        await CriarAsync(marcador, criadoEm: new DateTimeOffset(2026, 1, 10, 23, 59, 59, TimeSpan.Zero));
        await CriarAsync(marcador, criadoEm: new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero), titulo: "dentro");
        await CriarAsync(marcador, criadoEm: new DateTimeOffset(2026, 1, 11, 23, 59, 59, TimeSpan.Zero), titulo: "dentro");
        await CriarAsync(marcador, criadoEm: new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero));

        var titulos = Valores(await ListarAsync($"q={marcador}&criadoDe=2026-01-11&criadoAte=2026-01-11"), "titulo");

        titulos.Count.ShouldBe(2);
        titulos.ShouldAllBe(t => t.StartsWith("dentro"));
    }

    [Fact]
    public async Task Listar_OrdenadoPorPrioridade_UsaOrdemDeNegocioENaoAlfabetica()
    {
        var marcador = Marcador();
        foreach (var prioridade in new[] { Prioridade.Media, Prioridade.Critica, Prioridade.Baixa, Prioridade.Alta })
        {
            await CriarAsync(marcador, prioridade: prioridade);
        }

        var desc = Valores(await ListarAsync($"q={marcador}&ordenarPor=prioridade"), "prioridade");
        var asc = Valores(await ListarAsync($"q={marcador}&ordenarPor=prioridade&direcao=asc"), "prioridade");

        desc.ShouldBe(["Critica", "Alta", "Media", "Baixa"]);
        asc.ShouldBe(["Baixa", "Media", "Alta", "Critica"]);
    }

    [Fact]
    public async Task Listar_OrdenadoPorData_RespeitaDirecao()
    {
        var marcador = Marcador();
        for (var i = 0; i < 3; i++)
        {
            await CriarAsync(marcador, criadoEm: _base.AddDays(i), titulo: $"dia{i}");
        }

        var padrao = Valores(await ListarAsync($"q={marcador}"), "titulo");
        var asc = Valores(await ListarAsync($"q={marcador}&ordenarPor=criadoEm&direcao=asc"), "titulo");

        padrao.Select(t => t[..4]).ShouldBe(["dia2", "dia1", "dia0"]);
        asc.Select(t => t[..4]).ShouldBe(["dia0", "dia1", "dia2"]);
    }

    [Fact]
    public async Task Listar_Paginado_DevolveAFatiaETotaisCorretos()
    {
        var marcador = Marcador();
        for (var i = 0; i < 25; i++)
        {
            await CriarAsync(marcador, criadoEm: _base.AddMinutes(i));
        }

        var terceira = await ListarAsync($"q={marcador}&pagina=3&tamanhoPagina=10");
        var alem = await ListarAsync($"q={marcador}&pagina=9&tamanhoPagina=10");

        terceira.GetProperty("itens").GetArrayLength().ShouldBe(5);
        terceira.GetProperty("pagina").GetInt32().ShouldBe(3);
        terceira.GetProperty("totalItens").GetInt32().ShouldBe(25);
        terceira.GetProperty("totalPaginas").GetInt32().ShouldBe(3);
        alem.GetProperty("itens").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("configuracao")]
    [InlineData("CONFIGURAÇÃO")]
    [InlineData("ção de not")]
    public async Task Listar_BuscaSemAcentoESemCaixa_EncontraConfiguracao(string termo)
    {
        var marcador = Marcador();
        await CriarAsync(marcador, descricao: "A configuração de notificações não é salva.");
        await CriarAsync(marcador, descricao: "Outro problema qualquer na tela.");

        var itens = await ListarAsync($"q={Uri.EscapeDataString(termo)}&criadoDe=2026-01-10&criadoAte=2026-01-10");
        var titulos = Valores(itens, "titulo").Where(t => t.Contains(marcador)).ToList();

        titulos.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Listar_BuscaPorCodigoParcial_EncontraERR504()
    {
        var marcador = Marcador();
        await CriarAsync(marcador, titulo: $"Erro ERR-504 no portal {marcador}");

        var titulos = Valores(await ListarAsync($"q=ERR-5&criadoDe=2026-01-10&criadoAte=2026-01-10"), "titulo");

        titulos.ShouldContain(t => t.Contains(marcador));
    }

    [Fact]
    public async Task Listar_BuscaComCuringas_TrataPorcentagemESublinhadoComoTextoLiteral()
    {
        var marcador = Marcador();
        await CriarAsync(marcador, descricao: "CPU em 100% desde cedo, tudo travado.");
        await CriarAsync(marcador, descricao: "CPU em 1000 processos, tudo travado.");
        await CriarAsync(marcador, descricao: "Arquivo nota_fiscal.xml corrompido aqui.");
        await CriarAsync(marcador, descricao: "Arquivo notaXfiscal.xml corrompido aqui.");

        var porcentagem = Valores(await ListarAsync($"q={Uri.EscapeDataString("100%")}&criadoDe=2026-01-10&criadoAte=2026-01-10"), "titulo")
            .Count(t => t.Contains(marcador));
        var sublinhado = Valores(await ListarAsync("q=nota_fiscal&criadoDe=2026-01-10&criadoAte=2026-01-10"), "titulo")
            .Count(t => t.Contains(marcador));

        porcentagem.ShouldBe(1);
        sublinhado.ShouldBe(1);
    }

    [Fact]
    public async Task Listar_BuscaPorTexto_ConsultaGeradaPeloEfUsaOIndiceTrigram()
    {
        var planos = new CapturaDePlanos();
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        var opcoes = new DbContextOptionsBuilder<HelpDeskDbContext>();
        ConfiguracaoBanco.Configurar(opcoes, banco.ConnectionString).AddInterceptors(planos);
        await using var db = new HelpDeskDbContext(opcoes.Options);
        var consulta = new HelpDesk.Infrastructure.Consultas.ConsultaChamados(db);
        var filtro = HelpDesk.Application.Chamados.ListarChamados.Validar(new([], [], [], false, "configuracao",
            null, null, null, null, null, null));

        await consulta.ListarAsync(filtro, Ct);

        // Com 200 linhas o planner prefere seq scan (correto). Desligado, o plano do COUNT, que só tem o filtro, prova
        // que a expressão gerada pelo EF é a mesma do índice. A consulta da página pode legitimamente preferir o
        // índice 1, que já entrega a ordem pedida com LIMIT.
        planos.Planos.Count.ShouldBe(2);
        var planoDoFiltro = planos.Planos[0];
        planoDoFiltro.ShouldContain("Aggregate");
        planoDoFiltro.ShouldContain("Index Scan on ix_chamados_busca_trgm");
    }

    [Theory]
    [InlineData("q=ab")]
    [InlineData("q=" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("pagina=0")]
    [InlineData("tamanhoPagina=0")]
    [InlineData("tamanhoPagina=101")]
    [InlineData("ordenarPor=titulo")]
    [InlineData("direcao=cima")]
    [InlineData("status=Pendente")]
    [InlineData("status=9")]
    [InlineData("pagina=abc")]
    [InlineData("criadoDe=2026-02-01&criadoAte=2026-01-01")]
    [InlineData("criadoDe=01/02/2026x")]
    public async Task Listar_ParametroInvalido_Retorna400RequisicaoInvalida(string query)
    {
        using var resposta = await api.CreateClient().GetAsync($"/api/chamados?{query}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("requisicao_invalida");
    }

    // ---------- Apoio ----------

    private static string Marcador() => $"zq{Guid.NewGuid():N}"[..14];

    private async Task<JsonElement> ListarAsync(string query)
    {
        using var resposta = await api.CreateClient().GetAsync($"/api/chamados?{query}", Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync(Ct));
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        return json.RootElement.Clone();
    }

    private static List<string> Valores(JsonElement pagina, string campo) =>
        [.. pagina.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty(campo).GetString()!)];

    /// <summary>Grava direto pelo domínio, para controlar data de criação e status.</summary>
    private async Task CriarAsync(
        string marcador,
        StatusChamado status = StatusChamado.Aberto,
        Prioridade prioridade = Prioridade.Media,
        short? categoriaId = null,
        DateTimeOffset? criadoEm = null,
        string titulo = "Chamado",
        string descricao = "Descrição do chamado de teste.")
    {
        var criacao = criadoEm ?? _base;
        var chamado = Chamado.Abrir($"{titulo} {marcador}", descricao, "Maria Exemplo", "maria@example.com",
            categoriaId, prioridade, criacao);
        StatusChamado[] caminho = status switch
        {
            StatusChamado.EmAndamento => [StatusChamado.EmAndamento],
            StatusChamado.Cancelado => [StatusChamado.Cancelado],
            _ => [],
        };
        foreach (var passo in caminho)
        {
            chamado.MudarStatus(passo, "Ana", null, criacao.AddMinutes(1));
        }

        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Chamados.Add(chamado);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>Roda o EXPLAIN de cada consulta com busca, com o seq scan desligado na mesma conexão.</summary>
    private sealed class CapturaDePlanos : DbCommandInterceptor
    {
        public List<string> Planos { get; } = [];

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            await ExplicarAsync(command, result, cancellationToken);

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default) =>
            await ExplicarAsync(command, result, cancellationToken);

        private async Task<T> ExplicarAsync<T>(DbCommand command, T result, CancellationToken cancellationToken)
        {
            if (!command.CommandText.Contains("f_unaccent"))
            {
                return result;
            }

            await using var explain = command.Connection!.CreateCommand();
            explain.CommandText = $"SET enable_seqscan = off; EXPLAIN {command.CommandText}; RESET enable_seqscan;";
            foreach (DbParameter parametro in command.Parameters)
            {
                explain.Parameters.Add(((ICloneable)parametro).Clone());
            }

            var linhas = new List<string>();
            await using (var leitor = await explain.ExecuteReaderAsync(cancellationToken))
            {
                do
                {
                    while (await leitor.ReadAsync(cancellationToken))
                    {
                        linhas.Add(leitor.GetString(0));
                    }
                }
                while (await leitor.NextResultAsync(cancellationToken));
            }

            Planos.Add(string.Join('\n', linhas));
            return result;
        }
    }
}
