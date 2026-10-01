using HelpDesk.Application;
using HelpDesk.Domain.Chamados;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelpDesk.IntegrationTests.Persistencia;

public sealed class ChamadoPersistenciaTests(BancoFixture banco)
{
    private static readonly DateTimeOffset _inicio = new(2026, 9, 20, 13, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Salvar_ChamadoNovo_GeraNumeroEHistoricoInicial()
    {
        var chamado = await CriarAsync();

        var salvo = await ConsultarAsync(db => db.Chamados
            .Include(c => c.Historico)
            .SingleAsync(c => c.Id == chamado.Id, Ct));

        salvo.Numero.ShouldBeGreaterThan(0);
        salvo.Status.ShouldBe(StatusChamado.Aberto);
        salvo.Prioridade.ShouldBe(Prioridade.Media);
        salvo.CriadoEm.ShouldBe(_inicio);
        var historico = salvo.Historico.ShouldHaveSingleItem();
        historico.StatusAnterior.ShouldBeNull();
        historico.StatusNovo.ShouldBe(StatusChamado.Aberto);
    }

    [Fact]
    public async Task Salvar_ResolucaoComComentario_GravaStatusHistoricoEComentarioJuntos()
    {
        var chamado = await CriarAsync();
        await AlterarAsync(chamado.Id, c => c.MudarStatus(StatusChamado.EmAndamento, "Ana", null, _inicio.AddHours(1)));
        await AlterarAsync(chamado.Id, c =>
            c.MudarStatus(StatusChamado.Resolvido, "Ana", "Permissão reaplicada.", _inicio.AddHours(2)));

        var salvo = await ConsultarAsync(db => db.Chamados
            .Include(c => c.Historico)
            .Include(c => c.Comentarios)
            .SingleAsync(c => c.Id == chamado.Id, Ct));

        salvo.Status.ShouldBe(StatusChamado.Resolvido);
        salvo.ResolvidoEm.ShouldBe(_inicio.AddHours(2));
        salvo.Historico.OrderBy(h => h.AlteradoEm).Select(h => h.StatusNovo)
            .ShouldBe([StatusChamado.Aberto, StatusChamado.EmAndamento, StatusChamado.Resolvido]);
        salvo.Comentarios.ShouldHaveSingleItem().Texto.ShouldBe("Permissão reaplicada.");
    }

    [Fact]
    public async Task Salvar_AlteracaoConcorrente_LancaDbUpdateConcurrencyException()
    {
        var chamado = await CriarAsync();
        await using var servicos = banco.CriarServicos();
        await using var escopoA = servicos.CreateAsyncScope();
        await using var escopoB = servicos.CreateAsyncScope();
        var dbA = escopoA.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var dbB = escopoB.ServiceProvider.GetRequiredService<HelpDeskDbContext>();

        // Dois atendentes abrem a mesma versão do chamado.
        var doAtendenteA = await dbA.Chamados.Include(c => c.Historico).SingleAsync(c => c.Id == chamado.Id, Ct);
        var doAtendenteB = await dbB.Chamados.Include(c => c.Historico).SingleAsync(c => c.Id == chamado.Id, Ct);
        var versaoInicial = dbA.Entry(doAtendenteA).Property<uint>(HelpDeskDbContext.VersaoChamado).CurrentValue;

        doAtendenteA.MudarStatus(StatusChamado.EmAndamento, "Ana", null, _inicio.AddHours(1));
        await dbA.SaveChangesAsync(Ct);
        doAtendenteB.MudarStatus(StatusChamado.Cancelado, "Bruno", null, _inicio.AddHours(1));

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync(Ct));
        dbA.Entry(doAtendenteA).Property<uint>(HelpDeskDbContext.VersaoChamado).CurrentValue.ShouldNotBe(versaoInicial);
    }

    [Fact]
    public async Task Repositorio_GravacaoQuePerdeACorrida_LancaVersaoDesatualizada()
    {
        var chamado = await CriarAsync();
        await using var servicos = banco.CriarServicos();
        await using var escopoA = servicos.CreateAsyncScope();
        await using var escopoB = servicos.CreateAsyncScope();
        var repositorioA = new RepositorioChamados(escopoA.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
        var repositorioB = new RepositorioChamados(escopoB.ServiceProvider.GetRequiredService<HelpDeskDbContext>());

        // Sem If-Match: os dois leem a mesma versão e gravam em sequência.
        var lidoPorA = (await repositorioA.ObterParaAlteracaoAsync(chamado.Id, Ct))!;
        var lidoPorB = (await repositorioB.ObterParaAlteracaoAsync(chamado.Id, Ct))!;
        repositorioA.Versao(lidoPorA).ShouldBe(repositorioB.Versao(lidoPorB));
        lidoPorA.MudarStatus(StatusChamado.EmAndamento, "Ana", null, _inicio.AddHours(1));
        await repositorioA.SalvarAsync(Ct);
        lidoPorB.Comentar("Bruno", "Comentário que perdeu a corrida.", _inicio.AddHours(1));

        await Should.ThrowAsync<VersaoDesatualizadaException>(() => repositorioB.SalvarAsync(Ct));
        var salvo = await ConsultarAsync(db => db.Chamados.Include(c => c.Comentarios).SingleAsync(c => c.Id == chamado.Id, Ct));
        salvo.Comentarios.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("UPDATE chamados SET prioridade = 'critica', status = 'cancelado' WHERE id = @id",
        "ck_chamados_critica_nao_cancelada")]
    [InlineData("UPDATE chamados SET status = 'resolvido' WHERE id = @id", "ck_chamados_resolvido_em_coerente")]
    [InlineData("UPDATE chamados SET resolvido_em = now() WHERE id = @id", "ck_chamados_resolvido_em_coerente")]
    [InlineData("UPDATE chamados SET status = 'resolvido', resolvido_em = criado_em - interval '1 hour' WHERE id = @id",
        "ck_chamados_resolvido_em_apos_criacao")]
    [InlineData("UPDATE chamados SET titulo = 'abc' WHERE id = @id", "ck_chamados_titulo_tamanho")]
    [InlineData("UPDATE chamados SET descricao = 'curta' WHERE id = @id", "ck_chamados_descricao_tamanho")]
    [InlineData("UPDATE chamados SET solicitante_email = 'sem-arroba' WHERE id = @id",
        "ck_chamados_solicitante_email_formato")]
    [InlineData("""
        INSERT INTO historico_status (chamado_id, status_anterior, status_novo, alterado_em, alterado_por)
        VALUES (@id, 'aberto', 'aberto', now(), 'Ana')
        """, "ck_historico_status_muda_status")]
    [InlineData("""
        INSERT INTO comentarios (id, chamado_id, autor, texto, criado_em)
        VALUES (gen_random_uuid(), @id, 'Ana', '', now())
        """, "ck_comentarios_texto_tamanho")]
    public async Task EscritaForaDaApi_ViolandoRegra_ERejeitadaPeloBanco(string sql, string constraint)
    {
        var chamado = await CriarAsync();

        var erro = await Should.ThrowAsync<PostgresException>(() => ConsultarAsync(db =>
            db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("id", chamado.Id)], Ct)));

        erro.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        erro.ConstraintName.ShouldBe(constraint);
    }

    [Fact]
    public async Task Migration_Aplicada_CriaOsIndicesDoModelo()
    {
        var indices = await ConsultarAsync(db => db.Database
            .SqlQuery<string>($"""
                SELECT indexname AS "Value" FROM pg_indexes
                WHERE tablename IN ('chamados', 'comentarios', 'historico_status')
                """)
            .ToListAsync(Ct));

        string[] esperados =
        [
            "ix_chamados_criado_em_id", "ix_chamados_status_criado_em", "ix_chamados_prioridade_criado_em",
            "ix_chamados_categoria_id_criado_em", "ix_chamados_busca_trgm", "ux_chamados_numero",
            "ix_comentarios_chamado_id_criado_em", "ix_historico_status_chamado_id_alterado_em",
        ];
        esperados.ShouldBeSubsetOf(indices);
    }

    [Fact]
    public async Task FUnaccent_TextoComAcento_RemoveAcentos()
    {
        var resultado = await ConsultarAsync(db => db.Database
            .SqlQuery<string>($"SELECT f_unaccent(lower('Configuração Não Concluída')) AS \"Value\"")
            .SingleAsync(Ct));

        resultado.ShouldBe("configuracao nao concluida");
    }

    // ---------- Apoio ----------

    private async Task<Chamado> CriarAsync()
    {
        var chamado = Chamado.Abrir(
            "Erro ao emitir boleto",
            "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo",
            "maria@example.com",
            null,
            null,
            _inicio);

        await ConsultarAsync(async db =>
        {
            db.Chamados.Add(chamado);
            return await db.SaveChangesAsync(Ct);
        });
        return chamado;
    }

    private Task AlterarAsync(Guid id, Action<Chamado> alteracao) =>
        ConsultarAsync(async db =>
        {
            var chamado = await db.Chamados
                .Include(c => c.Historico)
                .Include(c => c.Comentarios)
                .SingleAsync(c => c.Id == id, Ct);
            alteracao(chamado);
            return await db.SaveChangesAsync(Ct);
        });

    private async Task<T> ConsultarAsync<T>(Func<HelpDeskDbContext, Task<T>> consulta)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }
}
