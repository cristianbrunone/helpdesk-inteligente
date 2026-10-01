using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using HelpDesk.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.IntegrationTests.Conhecimento;

/// <summary>
/// Busca semântica no pgvector com o embedding fake, sobre o seed já indexado pelo reconciliador. Um banco isolado
/// para a classe: o índice é montado uma vez e só lido pelos testes.
/// </summary>
public sealed class BuscaSemanticaTests(BuscaSemanticaTests.IndiceDoSeed indice)
    : IClassFixture<BuscaSemanticaTests.IndiceDoSeed>
{
    private const string Consulta = "Erro 403 em boletos\nAo abrir o módulo de boletos aparece erro 403, acesso negado.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Buscar_Erro403EmBoletos_RecuperaOArtigoFinanceiroEChamadosSemelhantes()
    {
        var documentos = await BuscarAsync(Consulta, _padrao);

        // Critério da Sprint 3, com o fake: o melhor resultado é o artigo financeiro sobre 403 em boletos.
        var primeiro = documentos[0];
        primeiro.Tipo.ShouldBe(DocumentoRecuperado.TipoArtigo);
        primeiro.Titulo.ShouldBe("Erro 403 no módulo de boletos");
        (await CategoriaDoArtigoAsync(primeiro.Id)).ShouldBe("Financeiro");
        documentos.ShouldContain(d => d.Tipo == DocumentoRecuperado.TipoChamado && d.Titulo.Contains("403")
            && d.Numero != null);
        documentos.Select(d => d.Similaridade).ShouldBeInOrder(SortDirection.Descending);
        documentos.ShouldAllBe(d => d.Similaridade >= _padrao.SimilaridadeMinima);
    }

    [Fact]
    public async Task Buscar_Sempre_DevolveNoMaximoTopKDeCadaTipo()
    {
        var documentos = await BuscarAsync(Consulta, new OpcoesRag(TopK: 2, SimilaridadeMinima: 0));

        documentos.Count(d => d.Tipo == DocumentoRecuperado.TipoChamado).ShouldBe(2);
        documentos.Count(d => d.Tipo == DocumentoRecuperado.TipoArtigo).ShouldBe(2);
    }

    [Fact]
    public async Task Buscar_ConteudoRecuperado_EOMascaradoDoIndice()
    {
        var documentos = await BuscarAsync("Meu CPF e e-mail no cadastro de clientes", new OpcoesRag(20, 0));

        documentos.ShouldNotBeEmpty();
        documentos.ShouldAllBe(d => !d.ConteudoMascarado.Contains("@example.com"));
    }

    [Fact]
    public async Task Buscar_AbaixoDoLimiar_NaoDevolveNada()
    {
        (await BuscarAsync(Consulta, new OpcoesRag(3, 0.99))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Buscar_ComOutroModelo_IgnoraOsVetoresDoModeloIndexado()
    {
        // Vetores de modelos diferentes não são comparáveis (ADR-0011): durante uma reindexação, nada é misturado.
        (await BuscarAsync(Consulta, new OpcoesRag(3, 0), modelo: "outro-modelo")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Buscar_PlanoDaConsulta_UsaOIndiceHnswPorCosseno()
    {
        await using var servicos = indice.Servicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        await using var conexao = db.Database.GetDbConnection();
        await conexao.OpenAsync(Ct);
        await using var transacao = await conexao.BeginTransactionAsync(Ct);
        await using var comando = conexao.CreateCommand();
        comando.Transaction = transacao;
        // Com ~200 linhas o planner prefere varrer a tabela; desligar o seq scan mostra que o índice serve à busca.
        comando.CommandText = """
            SET LOCAL enable_seqscan = off;
            EXPLAIN SELECT id FROM documentos_rag
            WHERE embedding_modelo = 'fake-embedding-v1'
            ORDER BY embedding <=> (SELECT embedding FROM documentos_rag LIMIT 1)
            LIMIT 3
            """;
        var plano = new List<string>();
        await using (var leitor = await comando.ExecuteReaderAsync(Ct))
        {
            while (await leitor.ReadAsync(Ct))
            {
                plano.Add(leitor.GetString(0));
            }
        }

        plano.ShouldContain(linha => linha.Contains("ix_documentos_rag_embedding_hnsw"));
    }

    private static readonly OpcoesRag _padrao = new(OpcoesRag.TopKPadrao, OpcoesRag.SimilaridadeMinimaPadrao);

    private async Task<string> CategoriaDoArtigoAsync(Guid artigoId)
    {
        await using var servicos = indice.Servicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        return await db.Artigos.Where(a => a.Id == artigoId)
            .Join(db.Categorias, a => a.CategoriaId, c => (short?)c.Id, (_, c) => c.Nome)
            .SingleAsync(Ct);
    }

    private async Task<IReadOnlyList<DocumentoRecuperado>> BuscarAsync(string texto, OpcoesRag opcoes, string? modelo = null)
    {
        await using var servicos = indice.Servicos();
        await using var escopo = servicos.CreateAsyncScope();
        var gerador = escopo.ServiceProvider.GetRequiredService<IGeradorEmbeddings>();
        var vetor = (await gerador.GerarAsync([new MascaradorDadosPessoais().Mascarar(texto)], Ct))[0];
        return await escopo.ServiceProvider.GetRequiredService<IBuscaSemantica>()
            .BuscarAsync(vetor, modelo ?? gerador.Modelo, opcoes, Ct);
    }

    /// <summary>O seed num banco isolado, indexado com o fake pelo reconciliador do Worker.</summary>
    public sealed class IndiceDoSeed(BancoFixture banco) : IAsyncLifetime
    {
        public string ConnectionString { get; private set; } = "";

        public ServiceProvider Servicos()
        {
            var services = new ServiceCollection();
            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
            services.AdicionarTriagem(ConnectionString, new OpcoesIA(true, true), new OpcoesLlm
            {
                Provedor = TipoProvedorLlm.Fake,
                ModeloChat = "x",
                Timeout = TimeSpan.FromSeconds(5),
                MaxRetries = 0,
                MaxTokensSaidaTriagem = 800,
            }, new OpcoesFila(5, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5), OpcoesFila.MaxReservasPadrao));
            services.AdicionarIndexacao(new OpcoesReconciliacao(TimeSpan.FromSeconds(30), 64));
            return services.BuildServiceProvider();
        }

        public async ValueTask InitializeAsync()
        {
            ConnectionString = await banco.CriarBancoMigradoAsync(default);
            await using var servicos = Servicos();
            var reconciliador = servicos.GetRequiredService<ReconciliadorIndexacao>();
            while (await reconciliador.ExecutarPassadaAsync(default) != new ResultadoReconciliacao(0, 0, 0))
            {
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
