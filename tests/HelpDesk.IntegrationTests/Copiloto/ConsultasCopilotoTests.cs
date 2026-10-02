using HelpDesk.Application.Copiloto;
using HelpDesk.Application.Triagem;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Conhecimento;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Copiloto;

/// <summary>
/// As leituras das ferramentas do copiloto contra o PostgreSQL real. As buscas usam o seed indexado com o embedding
/// fake (banco próprio da classe); o histórico, o banco compartilhado; as métricas, uma massa controlada num banco
/// vazio, com os números calculados à mão.
/// </summary>
public sealed class ConsultasCopilotoTests(BancoFixture banco, BuscaSemanticaTests.IndiceDoSeed indice)
    : IClassFixture<BuscaSemanticaTests.IndiceDoSeed>
{
    private const string Consulta = "Erro 403 em boletos\nAo abrir o módulo de boletos aparece erro 403, acesso negado.";
    private static readonly DateTimeOffset _base = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BuscarChamadosSimilares_Erro403EmBoletos_DevolveChamadosResolvidosParecidosEmOrdem()
    {
        var chamados = await NoIndiceAsync((consultas, vetor, modelo) =>
            consultas.BuscarChamadosSimilaresAsync(vetor, modelo, null, 3, 0.35, Guid.Empty, Ct));

        chamados.Count.ShouldBeInRange(1, 3);
        chamados.ShouldContain(c => c.Titulo.Contains("403"));
        chamados.Select(c => c.Similaridade).ShouldBeInOrder(SortDirection.Descending);
        chamados.ShouldAllBe(c => c.Similaridade >= 0.35 && c.Numero > 0);
    }

    [Fact]
    public async Task BuscarChamadosSimilares_ChamadoEmContexto_NuncaVoltaComoSemelhanteDeSiMesmo()
    {
        var primeiro = (await NoIndiceAsync((consultas, vetor, modelo) =>
            consultas.BuscarChamadosSimilaresAsync(vetor, modelo, null, 5, 0, Guid.Empty, Ct)))[0];

        var semEle = await NoIndiceAsync((consultas, vetor, modelo) =>
            consultas.BuscarChamadosSimilaresAsync(vetor, modelo, null, 5, 0, primeiro.Id, Ct));

        semEle.Count.ShouldBe(5); // a varredura iterativa completa o LIMIT mesmo com o filtro
        semEle.ShouldNotContain(c => c.Id == primeiro.Id);
    }

    [Theory]
    [InlineData((short)1)] // Acesso/Login
    [InlineData((short)2)] // Financeiro
    public async Task BuscarChamadosSimilares_ComCategoria_SoDevolveChamadosDela(short categoriaId)
    {
        var chamados = await NoIndiceAsync((consultas, vetor, modelo) =>
            consultas.BuscarChamadosSimilaresAsync(vetor, modelo, categoriaId, 5, 0, Guid.Empty, Ct));

        chamados.Count.ShouldBe(5);
        var categorias = await NoBancoDoIndiceAsync(db => db.Chamados
            .Where(c => chamados.Select(s => s.Id).Contains(c.Id))
            .Select(c => c.CategoriaId)
            .Distinct()
            .ToListAsync(Ct));
        categorias.ShouldBe([categoriaId]);
    }

    [Fact]
    public async Task BuscarChamadosSimilares_ComOutroModelo_NaoDevolveNada() =>
        (await NoIndiceAsync((consultas, vetor, _) =>
            consultas.BuscarChamadosSimilaresAsync(vetor, "outro-modelo", null, 5, 0, Guid.Empty, Ct)))
        .ShouldBeEmpty();

    [Fact]
    public async Task BuscarArtigos_Erro403EmBoletos_OPrimeiroEOArtigoDeBoletosENoMaximoOLimite()
    {
        var trechos = await NoIndiceAsync((consultas, vetor, modelo) =>
            consultas.BuscarArtigosAsync(vetor, modelo, 2, 0.35, Ct));

        trechos.Count.ShouldBeInRange(1, 2);
        trechos[0].Titulo.ShouldBe("Erro 403 no módulo de boletos");
        trechos[0].ConteudoMascarado.ShouldStartWith("Artigo: Erro 403 no módulo de boletos");
    }

    [Fact]
    public async Task BuscarArtigos_ArtigoDesativadoAindaNoIndice_NaoEDevolvido()
    {
        await using var servicos = indice.Servicos();
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var consultas = escopo.ServiceProvider.GetRequiredService<IConsultasCopiloto>();
        var vetor = await VetorAsync(escopo.ServiceProvider);

        // Desativado e ainda não removido pelo reconciliador; a transação é desfeita, e o índice fica intacto.
        await using var transacao = await db.Database.BeginTransactionAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE artigos_conhecimento SET ativo = false WHERE titulo = 'Erro 403 no módulo de boletos'", Ct);

        var trechos = await consultas.BuscarArtigosAsync(vetor, "fake-embedding-v1", 5, 0, Ct);

        trechos.ShouldNotBeEmpty();
        trechos.ShouldNotContain(t => t.Titulo == "Erro 403 no módulo de boletos");
        await transacao.RollbackAsync(Ct);
    }

    [Fact]
    public async Task ObterHistorico_ChamadoComMudancasEComentarios_DevolveTudoEmOrdemCronologica()
    {
        var chamado = Chamado.Abrir("Impressora do financeiro offline", "A impressora parou depois da troca de toner.",
            "Maria Souza", "maria@example.com", 5, Prioridade.Media, _base);
        chamado.MudarStatus(StatusChamado.EmAndamento, "Ana (suporte)", null, _base.AddHours(1));
        chamado.Comentar("Ana (suporte)", "Toner reinstalado; aguardando teste.", _base.AddHours(2));
        chamado.Comentar("Maria Souza", "Ainda não imprime.", _base.AddHours(3));
        await using (var servicos = banco.CriarServicos())
        await using (var escopo = servicos.CreateAsyncScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            db.Chamados.Add(chamado);
            await db.SaveChangesAsync(Ct);
        }

        var historico = (await NoBancoCompartilhadoAsync(c => c.ObterHistoricoAsync(chamado.Id, Ct))).ShouldNotBeNull();

        historico.Numero.ShouldBe(chamado.Numero);
        historico.Status.ShouldBe(StatusChamado.EmAndamento);
        historico.SolicitanteNome.ShouldBe("Maria Souza");
        historico.Mudancas.ShouldBe(
        [
            new(null, StatusChamado.Aberto, _base),
            new(StatusChamado.Aberto, StatusChamado.EmAndamento, _base.AddHours(1)),
        ]);
        historico.Comentarios.ShouldBe(
        [
            new("Toner reinstalado; aguardando teste.", _base.AddHours(2)),
            new("Ainda não imprime.", _base.AddHours(3)),
        ]);
    }

    [Fact]
    public async Task ObterHistorico_ChamadoInexistente_DevolveNulo() =>
        (await NoBancoCompartilhadoAsync(c => c.ObterHistoricoAsync(Guid.CreateVersion7(), Ct))).ShouldBeNull();

    [Fact]
    public async Task ObterMetricas_MassaControlada_BatemComOsNumerosCalculadosAMao()
    {
        var connectionString = await BancoComMassaAsync();

        var alfa = await MetricasAsync(connectionString, 1);
        var beta = await MetricasAsync(connectionString, 2);
        var gama = await MetricasAsync(connectionString, 3);

        // Alfa: 3 chamados (um cancelado), 2 resolvidos em 2 h e 4 h; 2 aceitas e 1 rejeitada.
        alfa.ShouldBe(new MetricasCategoria(3, 2, 3.0, 2, 1));
        // Beta: o fechado conta como resolvido (RN-13), em 10 h; o aberto não.
        beta.ShouldBe(new MetricasCategoria(2, 1, 10.0, 1, 0));
        // Gama: sem chamados e só uma triagem concluída sem decisão, que não entra (RN-12).
        gama.ShouldBe(new MetricasCategoria(0, 0, null, 0, 0));
    }

    // ---------- Auxiliares ----------

    private async Task<IReadOnlyList<T>> NoIndiceAsync<T>(
        Func<IConsultasCopiloto, float[], string, Task<IReadOnlyList<T>>> consulta)
    {
        await using var servicos = indice.Servicos();
        await using var escopo = servicos.CreateAsyncScope();
        var vetor = await VetorAsync(escopo.ServiceProvider);
        return await consulta(escopo.ServiceProvider.GetRequiredService<IConsultasCopiloto>(), vetor, "fake-embedding-v1");
    }

    private async Task<T> NoBancoDoIndiceAsync<T>(Func<HelpDeskDbContext, Task<T>> consulta)
    {
        await using var servicos = indice.Servicos();
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }

    private async Task<T> NoBancoCompartilhadoAsync<T>(Func<IConsultasCopiloto, Task<T>> consulta)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<IConsultasCopiloto>());
    }

    private static async Task<float[]> VetorAsync(IServiceProvider servicos)
    {
        var gerador = servicos.GetRequiredService<HelpDesk.Application.Conhecimento.IGeradorEmbeddings>();
        gerador.Modelo.ShouldBe("fake-embedding-v1");
        return (await gerador.GerarAsync([new MascaradorDadosPessoais().Mascarar(Consulta)], Ct))[0];
    }

    private async Task<MetricasCategoria> MetricasAsync(string connectionString, short categoriaId)
    {
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<IConsultasCopiloto>()
            .ObterMetricasDaCategoriaAsync(categoriaId, Ct);
    }

    private async Task<string> BancoComMassaAsync()
    {
        var connectionString = await banco.CriarBancoVazioAsync(Ct);
        await using var servicos = banco.CriarServicos(connectionString);
        await using var escopo = servicos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        await db.Database.MigrateAsync(Ct);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO categorias (id, nome) VALUES (1, 'Alfa'), (2, 'Beta'), (3, 'Gama');

            WITH base AS (SELECT now() - interval '20 days' AS t0)
            INSERT INTO chamados (id, titulo, descricao, solicitante_nome, solicitante_email, categoria_id,
                                  prioridade, status, criado_em, atualizado_em, resolvido_em)
            SELECT id::uuid, 'Chamado de teste', 'Descrição do chamado de teste.', 'Pessoa', 'p@example.com',
                   categoria, 'media', status::status_chamado, t0, t0, t0 + resolucao
            FROM base, (VALUES
                ('00000000-0000-7000-8000-000000000001', 1, 'resolvido', interval '2 hours'),
                ('00000000-0000-7000-8000-000000000002', 1, 'resolvido', interval '4 hours'),
                ('00000000-0000-7000-8000-000000000003', 2, 'fechado',   interval '10 hours'),
                ('00000000-0000-7000-8000-000000000004', 2, 'aberto',    NULL),
                ('00000000-0000-7000-8000-000000000005', NULL, 'aberto', NULL),
                ('00000000-0000-7000-8000-000000000006', 1, 'cancelado', NULL)
            ) AS v(id, categoria, status, resolucao);

            INSERT INTO triagens_ia (id, chamado_id, status, categoria_sugerida_id, prioridade_sugerida, resumo,
                                     resposta_sugerida, confianca, provedor, modelo, prompt_versao, tentativas,
                                     proxima_tentativa_em, criado_em, concluida_em, decidida_em, decidida_por)
            SELECT gen_random_uuid(), chamado::uuid, status::status_triagem, sugerida, 'media', 'Resumo.', 'Olá!',
                   0.8, 'fake', 'fake-triagem-v1', 'triagem.v1', 0, now(), now(), now(),
                   CASE WHEN status IN ('aceita', 'rejeitada') THEN now() END,
                   CASE WHEN status IN ('aceita', 'rejeitada') THEN 'Ana' END
            FROM (VALUES
                ('00000000-0000-7000-8000-000000000001', 'aceita',    1),
                ('00000000-0000-7000-8000-000000000002', 'aceita',    1),
                ('00000000-0000-7000-8000-000000000006', 'rejeitada', 1),
                ('00000000-0000-7000-8000-000000000003', 'aceita',    2),
                ('00000000-0000-7000-8000-000000000004', 'concluida', 3)
            ) AS v(chamado, status, sugerida);
            """, Ct);
        return connectionString;
    }
}
