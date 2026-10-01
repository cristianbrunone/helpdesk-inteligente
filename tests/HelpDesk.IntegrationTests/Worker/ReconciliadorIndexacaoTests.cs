using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using HelpDesk.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.IntegrationTests.Worker;

/// <summary>
/// O reconciliador do índice do RAG (ADR-0010) contra o PostgreSQL real com o seed, cada teste no seu banco
/// isolado: ele indexa tudo o que encontra.
/// </summary>
public sealed class ReconciliadorIndexacaoTests(BancoFixture banco)
{
    private static readonly DateTimeOffset _agora = DateTimeOffset.UtcNow;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Passadas_AposASubida_IndexamTodosOsResolvidosEArtigosDoSeed()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await using var worker = Worker(bancoIsolado);

        await EsvaziarAsync(worker);

        var (resolvidos, artigos, documentos, semVetor, outroModelo) = await LerAsync(bancoIsolado, async db => (
            await db.Chamados.CountAsync(c => c.Status == StatusChamado.Resolvido || c.Status == StatusChamado.Fechado, Ct),
            await db.Artigos.CountAsync(Ct),
            await db.DocumentosRag.ToListAsync(Ct),
            await db.DocumentosRag.CountAsync(d => d.Embedding == null, Ct),
            await db.DocumentosRag.CountAsync(d => d.EmbeddingModelo != OpcoesLlm.ModeloEmbeddingFake, Ct)));

        documentos.Count(d => d.ChamadoId != null).ShouldBe(resolvidos);
        documentos.Where(d => d.ArtigoId != null).Select(d => d.ArtigoId).Distinct().Count().ShouldBe(artigos);
        documentos.Count(d => d.ArtigoId != null).ShouldBeGreaterThan(artigos); // vários chunks por artigo
        semVetor.ShouldBe(0);
        outroModelo.ShouldBe(0);
        // RN-11: o conteúdo indexado é o mascarado (o seed tem e-mails e CPFs falsos nas descrições).
        documentos.ShouldAllBe(d => !d.ConteudoMascarado.Contains("@example.com"));
        (await worker.Reconciliador.ExecutarPassadaAsync(Ct)).ShouldBe(new ResultadoReconciliacao(0, 0, 0));
    }

    [Fact]
    public async Task Reabrir_ChamadoIndexado_RemoveODocumentoNaProximaPassada()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await using var worker = Worker(bancoIsolado);
        await EsvaziarAsync(worker);
        var chamadoId = await AlterarResolvidoAsync(bancoIsolado,
            c => c.MudarStatus(StatusChamado.EmAndamento, "Ana (suporte)", "O problema voltou.", _agora));

        var resultado = await worker.Reconciliador.ExecutarPassadaAsync(Ct);

        resultado.Removidos.ShouldBe(1);
        (await LerAsync(bancoIsolado, db => db.DocumentosRag.AnyAsync(d => d.ChamadoId == chamadoId, Ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task Comentar_ChamadoResolvido_ReindexaComONovoConteudo()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await using var worker = Worker(bancoIsolado);
        await EsvaziarAsync(worker);
        var chamadoId = await AlterarResolvidoAsync(bancoIsolado,
            c => c.Comentar("Ana (suporte)", "Complemento: a causa foi o certificado vencido.", _agora));

        var resultado = await worker.Reconciliador.ExecutarPassadaAsync(Ct);

        resultado.ShouldBe(new ResultadoReconciliacao(0, 1, 1));
        var documento = await LerAsync(bancoIsolado, db => db.DocumentosRag.SingleAsync(d => d.ChamadoId == chamadoId, Ct));
        documento.ConteudoMascarado.ShouldContain("certificado vencido");
        documento.Embedding.ShouldNotBeNull();
    }

    [Fact]
    public async Task Fechar_ChamadoResolvido_ConfirmaODocumentoSemGerarVetorDeNovo()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var contador = new GeradorContado();
        await using var worker = Worker(bancoIsolado, contador);
        await EsvaziarAsync(worker);
        var antes = contador.Textos;
        var chamadoId = await AlterarResolvidoAsync(bancoIsolado,
            c => c.MudarStatus(StatusChamado.Fechado, "Ana (suporte)", null, _agora), StatusChamado.Resolvido);

        var resultado = await worker.Reconciliador.ExecutarPassadaAsync(Ct);

        resultado.ShouldBe(new ResultadoReconciliacao(0, 0, 0));
        contador.Textos.ShouldBe(antes);
        // Conferido: a passada seguinte não volta a montar este chamado.
        (await ChamadosParaConferirAsync(bancoIsolado)).ShouldNotContain(chamadoId);
    }

    [Fact]
    public async Task TrocarModelo_DeEmbedding_ReindexaTodosOsDocumentosSemDuplicar()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await using (var original = Worker(bancoIsolado))
        {
            await EsvaziarAsync(original);
        }

        var total = await LerAsync(bancoIsolado, db => db.DocumentosRag.CountAsync(Ct));
        var outro = new GeradorContado("outro-modelo-768");
        await using var trocado = Worker(bancoIsolado, outro);

        await EsvaziarAsync(trocado);

        outro.Textos.ShouldBe(total);
        var modelos = await LerAsync(bancoIsolado, db => db.DocumentosRag
            .GroupBy(d => d.EmbeddingModelo).Select(g => new { g.Key, Total = g.Count() }).ToListAsync(Ct));
        modelos.ShouldHaveSingleItem().Key.ShouldBe("outro-modelo-768");
        modelos[0].Total.ShouldBe(total);
    }

    [Fact]
    public async Task DesativarArtigo_RemoveTodosOsChunksDele()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        await using var worker = Worker(bancoIsolado);
        await EsvaziarAsync(worker);
        var artigoId = await LerAsync(bancoIsolado, async db =>
        {
            var artigo = await db.Artigos.OrderBy(a => a.Titulo).FirstAsync(Ct);
            artigo.Desativar(_agora);
            await db.SaveChangesAsync(Ct);
            return artigo.Id;
        });
        var chunks = await LerAsync(bancoIsolado, db => db.DocumentosRag.CountAsync(d => d.ArtigoId == artigoId, Ct));

        var resultado = await worker.Reconciliador.ExecutarPassadaAsync(Ct);

        resultado.Removidos.ShouldBe(chunks);
        (await LerAsync(bancoIsolado, db => db.DocumentosRag.AnyAsync(d => d.ArtigoId == artigoId, Ct))).ShouldBeFalse();
    }

    [Fact]
    public async Task DoisReconciliadoresConcorrentes_GeramOVetorDeCadaDocumentoUmaVezSo()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var contadorA = new GeradorContado();
        var contadorB = new GeradorContado();
        await using var a = Worker(bancoIsolado, contadorA, lote: 8);
        await using var b = Worker(bancoIsolado, contadorB, lote: 8);

        await Task.WhenAll(EsvaziarAsync(a), EsvaziarAsync(b));

        var total = await LerAsync(bancoIsolado, db => db.DocumentosRag.CountAsync(Ct));
        (await LerAsync(bancoIsolado, db => db.DocumentosRag.CountAsync(d => d.Embedding == null, Ct))).ShouldBe(0);
        (contadorA.Textos + contadorB.Textos).ShouldBe(total);
        contadorA.Textos.ShouldBeGreaterThan(0);
        contadorB.Textos.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ProvedorFora_DocumentosFicamPendentesERetomamQuandoVoltar()
    {
        var bancoIsolado = await banco.CriarBancoMigradoAsync(Ct);
        var instavel = new GeradorContado { Fora = true };
        await using var worker = Worker(bancoIsolado, instavel);

        await Should.ThrowAsync<ProvedorIndisponivelException>(() => worker.Reconciliador.ExecutarPassadaAsync(Ct));
        var pendentes = await LerAsync(bancoIsolado, db => db.DocumentosRag.CountAsync(d => d.Embedding == null, Ct));
        instavel.Fora = false;
        await EsvaziarAsync(worker);

        pendentes.ShouldBeGreaterThan(0); // a sincronização ficou gravada; só os vetores faltaram
        (await LerAsync(bancoIsolado, db => db.DocumentosRag.CountAsync(d => d.Embedding == null, Ct))).ShouldBe(0);
    }

    // ---------- Apoio ----------

    private static WorkerDeTeste Worker(string connectionString, GeradorContado? gerador = null, int lote = 32)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AdicionarTriagem(connectionString, new OpcoesIA(true, true), new OpcoesLlm
        {
            Provedor = TipoProvedorLlm.Fake,
            ModeloChat = "x",
            Timeout = TimeSpan.FromSeconds(5),
            MaxRetries = 0,
            MaxTokensSaidaTriagem = 800,
        }, new OpcoesFila(5, TimeSpan.FromMilliseconds(100), TimeSpan.FromMinutes(5), OpcoesFila.MaxReservasPadrao));
        services.AdicionarIndexacao(new OpcoesReconciliacao(TimeSpan.FromSeconds(30), lote));
        if (gerador is not null)
        {
            // Envolve o mesmo gerador que a composição usa (fake com resiliência e telemetria).
            services.AddSingleton<IGeradorEmbeddings>(sp => gerador.Envolver(new GeradorEmbeddings(
                sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                sp.GetRequiredService<OpcoesLlm>())));
        }

        return new WorkerDeTeste(services.BuildServiceProvider());
    }

    /// <summary>Passadas até não haver mais nada a fazer (o lote limita cada etapa).</summary>
    private static async Task EsvaziarAsync(WorkerDeTeste worker)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await worker.Reconciliador.ExecutarPassadaAsync(Ct) == new ResultadoReconciliacao(0, 0, 0))
            {
                return;
            }
        }

        throw new InvalidOperationException("O reconciliador não estabilizou em 100 passadas.");
    }

    /// <summary>Altera um chamado já indexado, pelo domínio, e devolve o ID.</summary>
    private Task<Guid> AlterarResolvidoAsync(
        string connectionString, Action<Chamado> alteracao, StatusChamado status = StatusChamado.Resolvido) =>
        LerAsync(connectionString, async db =>
        {
            var chamado = await db.Chamados.Include(c => c.Comentarios).Include(c => c.Historico)
                .Where(c => c.Status == status && db.DocumentosRag.Any(d => d.ChamadoId == c.Id))
                .OrderBy(c => c.Numero).FirstAsync(Ct);
            alteracao(chamado);
            await db.SaveChangesAsync(Ct);
            return chamado.Id;
        });

    private async Task<IReadOnlyList<Guid>> ChamadosParaConferirAsync(string connectionString)
    {
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        return await db.Chamados
            .Where(c => db.DocumentosRag.Any(d => d.ChamadoId == c.Id && d.OrigemAtualizadaEm < c.AtualizadoEm))
            .Select(c => c.Id).ToListAsync(Ct);
    }

    private async Task<T> LerAsync<T>(string connectionString, Func<HelpDeskDbContext, Task<T>> consulta)
    {
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }

    private sealed class WorkerDeTeste(ServiceProvider servicos) : IAsyncDisposable
    {
        public ReconciliadorIndexacao Reconciliador => servicos.GetRequiredService<ReconciliadorIndexacao>();

        public ValueTask DisposeAsync() => servicos.DisposeAsync();
    }

    /// <summary>
    /// Envolve o gerador real (fake): conta os textos vetorizados, pode trocar o nome do modelo (simula outro
    /// <c>LLM_EMBEDDING_MODEL</c>) e pode simular o provedor fora.
    /// </summary>
    private sealed class GeradorContado(string? modelo = null)
    {
        private int _textos;

        public string? ModeloSubstituto { get; } = modelo;

        public int Textos => _textos;

        public bool Fora { get; set; }

        public IGeradorEmbeddings Envolver(IGeradorEmbeddings interno) => new Envoltorio(this, interno);

        private sealed class Envoltorio(GeradorContado dono, IGeradorEmbeddings interno) : IGeradorEmbeddings
        {
            public string Modelo => dono.ModeloSubstituto ?? interno.Modelo;

            public Task<IReadOnlyList<float[]>> GerarAsync(
                IReadOnlyList<TextoMascarado> textos, CancellationToken cancellationToken)
            {
                if (dono.Fora)
                {
                    throw new ProvedorIndisponivelException(ProvedorIndisponivelException.TipoIndisponivel, "fora");
                }

                Interlocked.Add(ref dono._textos, textos.Count);
                return interno.GerarAsync(textos, cancellationToken);
            }
        }
    }
}
