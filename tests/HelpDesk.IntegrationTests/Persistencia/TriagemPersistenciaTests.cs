using HelpDesk.Application;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelpDesk.IntegrationTests.Persistencia;

public sealed class TriagemPersistenciaTests(BancoFixture banco)
{
    private static readonly DateTimeOffset _inicio = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly ExecucaoTriagem _execucao = new("fake", "fake-triagem-v1", "triagem.v1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Salvar_TriagemConcluidaEAceita_PersisteSugestaoEDecisao()
    {
        var (chamado, triagem) = await CriarAsync();
        await AlterarAsync(triagem.Id, chamado.Id, (c, t) =>
        {
            t.Concluir(new SugestaoTriagem(2, Prioridade.Alta, "Erro 403 em boletos.", "Olá!", 0.825m),
                _execucao, _inicio.AddSeconds(4));
            t.Aceitar(c, "Ana (suporte)", _inicio.AddMinutes(5));
        });

        var (chamadoSalvo, salva) = await LerAsync(chamado.Id, triagem.Id);

        salva.Status.ShouldBe(StatusTriagem.Aceita);
        salva.Confianca.ShouldBe(0.825m);
        salva.PrioridadeSugerida.ShouldBe(Prioridade.Alta);
        salva.PromptVersao.ShouldBe("triagem.v1");
        salva.TraceParent.ShouldBe("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        chamadoSalvo.CategoriaId.ShouldBe((short)2);
        chamadoSalvo.Prioridade.ShouldBe(Prioridade.Alta);
    }

    [Fact]
    public async Task Salvar_SegundaTriagemPendenteDoMesmoChamado_ERecusadaPeloIndiceUnico()
    {
        var (chamado, _) = await CriarAsync();

        var erro = await Should.ThrowAsync<DbUpdateException>(() => ConsultarAsync(async db =>
        {
            var salvo = await db.Chamados.SingleAsync(c => c.Id == chamado.Id, Ct);
            db.Triagens.Add(TriagemIA.Criar(salvo, _inicio.AddMinutes(1)));
            return await db.SaveChangesAsync(Ct);
        }));

        var postgres = erro.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("ux_triagens_ia_uma_pendente_por_chamado");
    }

    [Theory]
    [InlineData("UPDATE triagens_ia SET confianca = 1.5 WHERE id = @id", "ck_triagens_ia_confianca_faixa")]
    [InlineData("UPDATE triagens_ia SET resumo = repeat('x', 201) WHERE id = @id", "ck_triagens_ia_resumo_tamanho")]
    [InlineData("""
        UPDATE triagens_ia SET status = 'concluida', provedor = 'fake', modelo = 'm', prompt_versao = 'v'
        WHERE id = @id
        """, "ck_triagens_ia_sugestao_completa")]
    [InlineData("""
        UPDATE triagens_ia SET status = 'falhou', provedor = 'fake', modelo = 'm', prompt_versao = 'v'
        WHERE id = @id
        """, "ck_triagens_ia_falha_com_motivo")]
    [InlineData("UPDATE triagens_ia SET status = 'falhou', erro_motivo = 'x' WHERE id = @id",
        "ck_triagens_ia_execucao_registrada")]
    [InlineData("UPDATE triagens_ia SET decidida_em = now() WHERE id = @id", "ck_triagens_ia_decisao_coerente")]
    [InlineData("""
        INSERT INTO uso_llm (operacao, provedor, modelo, latencia_ms, sucesso, criado_em)
        VALUES ('resumo', 'fake', 'm', 10, true, now())
        """, "ck_uso_llm_operacao")]
    [InlineData("""
        INSERT INTO uso_llm (operacao, provedor, modelo, latencia_ms, sucesso, erro_tipo, criado_em)
        VALUES ('triagem', 'fake', 'm', 10, true, 'timeout', now())
        """, "ck_uso_llm_erro_coerente")]
    [InlineData("""
        INSERT INTO uso_llm (operacao, provedor, modelo, latencia_ms, sucesso, criado_em)
        VALUES ('triagem', 'fake', 'm', -1, true, now())
        """, "ck_uso_llm_valores_positivos")]
    public async Task EscritaForaDaApi_ViolandoRegra_ERejeitadaPeloBanco(string sql, string constraint)
    {
        var (_, triagem) = await CriarAsync();

        var erro = await Should.ThrowAsync<PostgresException>(() => ConsultarAsync(db =>
            db.Database.ExecuteSqlRawAsync(sql, [new NpgsqlParameter("id", triagem.Id)], Ct)));

        erro.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        erro.ConstraintName.ShouldBe(constraint);
    }

    [Fact]
    public async Task UsoLlm_TriagemApagada_ManteORegistroSemAReferencia()
    {
        var (chamado, triagem) = await CriarAsync();
        await ConsultarAsync(async db =>
        {
            db.UsoLlm.Add(new RegistroUsoLlm(RegistroUsoLlm.OperacaoTriagem, triagem.Id, chamado.Id, "fake",
                "fake-triagem-v1", 120, 40, 15, true, null, _inicio));
            return await db.SaveChangesAsync(Ct);
        });

        await ConsultarAsync(db => db.Chamados.Where(c => c.Id == chamado.Id).ExecuteDeleteAsync(Ct));

        var registro = await ConsultarAsync(db => db.UsoLlm.SingleAsync(u => u.ChamadoId == chamado.Id, Ct));
        registro.TriagemId.ShouldBeNull();
        registro.TokensEntrada.ShouldBe(120);
    }

    [Fact]
    public async Task Migration_Aplicada_CriaOsIndicesDaFilaEDoConsumo()
    {
        var indices = await ConsultarAsync(db => db.Database
            .SqlQuery<string>($"""
                SELECT indexdef AS "Value" FROM pg_indexes WHERE tablename IN ('triagens_ia', 'uso_llm')
                """)
            .ToListAsync(Ct));

        indices.ShouldContain(i => i.Contains("ix_triagens_ia_chamado_id_criado_em") && i.Contains("criado_em DESC"));
        indices.ShouldContain(i => i.Contains("ix_triagens_ia_fila") && i.Contains("WHERE (status = 'pendente'"));
        indices.ShouldContain(i => i.Contains("UNIQUE INDEX ux_triagens_ia_uma_pendente_por_chamado")
            && i.Contains("WHERE (status = 'pendente'"));
        indices.ShouldContain(i => i.Contains("ix_uso_llm_criado_em"));
    }

    // ---------- Corridas que o banco resolve ----------

    [Fact]
    public async Task Decidir_AceitarERejeitarAoMesmoTempo_OSegundoRecebeVersaoDesatualizada()
    {
        var (chamado, triagem) = await CriarAsync();
        await AlterarAsync(triagem.Id, chamado.Id, (_, t) => t.Concluir(
            new SugestaoTriagem(2, Prioridade.Alta, "Resumo.", "Olá!", 0.8m), _execucao, _inicio.AddSeconds(4)));
        await using var servicos = banco.CriarServicos();
        await using var escopoA = servicos.CreateAsyncScope();
        await using var escopoB = servicos.CreateAsyncScope();
        var dbA = escopoA.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var dbB = escopoB.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var chamadoA = await dbA.Chamados.SingleAsync(c => c.Id == chamado.Id, Ct);
        var triagemA = await dbA.Triagens.SingleAsync(t => t.Id == triagem.Id, Ct);
        var chamadoB = await dbB.Chamados.SingleAsync(c => c.Id == chamado.Id, Ct);
        var triagemB = await dbB.Triagens.SingleAsync(t => t.Id == triagem.Id, Ct);

        triagemA.Aceitar(chamadoA, "Ana", _inicio.AddMinutes(1));
        await new RepositorioChamados(dbA).SalvarAsync(Ct);
        triagemB.Rejeitar(chamadoB, "Bruno", null, _inicio.AddMinutes(1));

        await Should.ThrowAsync<VersaoDesatualizadaException>(() => new RepositorioChamados(dbB).SalvarAsync(Ct));
        var (_, salva) = await LerAsync(chamado.Id, triagem.Id);
        salva.Status.ShouldBe(StatusTriagem.Aceita);
        salva.DecididaPor.ShouldBe("Ana");
    }

    [Fact]
    public async Task Refazer_DuasPendentesAoMesmoTempo_OIndiceUnicoViraTriagemEmAndamento()
    {
        var (chamado, triagem) = await CriarAsync();
        await AlterarAsync(triagem.Id, chamado.Id, (_, t) => t.Falhar("motivo", _execucao, _inicio.AddSeconds(4)));
        await using var servicos = banco.CriarServicos();
        await using var escopoA = servicos.CreateAsyncScope();
        await using var escopoB = servicos.CreateAsyncScope();
        var dbA = escopoA.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var dbB = escopoB.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var chamadoA = await dbA.Chamados.SingleAsync(c => c.Id == chamado.Id, Ct);
        var chamadoB = await dbB.Chamados.SingleAsync(c => c.Id == chamado.Id, Ct);
        var vigenteA = await dbA.Triagens.SingleAsync(t => t.Id == triagem.Id, Ct);
        var vigenteB = await dbB.Triagens.SingleAsync(t => t.Id == triagem.Id, Ct);

        // As duas leram a vigente "Falhou" antes de qualquer uma gravar: o domínio deixa as duas passarem.
        dbA.Triagens.Add(TriagemIA.Refazer(chamadoA, vigenteA, _inicio.AddMinutes(1)));
        dbB.Triagens.Add(TriagemIA.Refazer(chamadoB, vigenteB, _inicio.AddMinutes(1)));
        await new RepositorioChamados(dbA).SalvarAsync(Ct);

        await Should.ThrowAsync<TriagemEmAndamentoException>(() => new RepositorioChamados(dbB).SalvarAsync(Ct));
    }

    // ---------- Apoio ----------

    private async Task<(Chamado, TriagemIA)> CriarAsync()
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo", "maria@example.com", null, null, _inicio);
        var triagem = TriagemIA.Criar(chamado, _inicio, "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
        await ConsultarAsync(async db =>
        {
            db.Chamados.Add(chamado);
            db.Triagens.Add(triagem);
            return await db.SaveChangesAsync(Ct);
        });
        return (chamado, triagem);
    }

    private Task AlterarAsync(Guid triagemId, Guid chamadoId, Action<Chamado, TriagemIA> alteracao) =>
        ConsultarAsync(async db =>
        {
            var chamado = await db.Chamados.SingleAsync(c => c.Id == chamadoId, Ct);
            var triagem = await db.Triagens.SingleAsync(t => t.Id == triagemId, Ct);
            alteracao(chamado, triagem);
            return await db.SaveChangesAsync(Ct);
        });

    private Task<(Chamado, TriagemIA)> LerAsync(Guid chamadoId, Guid triagemId) =>
        ConsultarAsync(async db => (
            await db.Chamados.SingleAsync(c => c.Id == chamadoId, Ct),
            await db.Triagens.SingleAsync(t => t.Id == triagemId, Ct)));

    private async Task<T> ConsultarAsync<T>(Func<HelpDeskDbContext, Task<T>> consulta)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }
}
