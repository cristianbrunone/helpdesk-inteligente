using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Conhecimento;
using HelpDesk.Infrastructure.Persistencia;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector;

namespace HelpDesk.IntegrationTests.Persistencia;

public sealed class RagPersistenciaTests(BancoFixture banco)
{
    private static readonly DateTimeOffset _agora = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string _hash = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Salvar_DocumentoIndexado_PreservaOVetorDe768Dimensoes()
    {
        var artigo = await CriarArtigoAsync();
        var vetor = new float[DocumentoRag.Dimensoes];
        vetor[3] = 0.6f;
        vetor[700] = 0.8f;
        var documento = DocumentoRag.DeArtigo(artigo.Id, 0, "Como liberar o módulo de boletos.", _hash, 2, _agora);
        documento.Indexar(new Vector(vetor), "fake-embedding-v1", _agora);
        await ConsultarAsync(db =>
        {
            db.DocumentosRag.Add(documento);
            return db.SaveChangesAsync(Ct);
        });

        var salvo = await ConsultarAsync(db => db.DocumentosRag.SingleAsync(d => d.Id == documento.Id, Ct));

        salvo.Embedding.ShouldNotBeNull().ToArray().ShouldBe(vetor);
        salvo.EmbeddingModelo.ShouldBe("fake-embedding-v1");
        salvo.HashConteudo.ShouldBe(_hash);
    }

    [Theory]
    [InlineData("NULL, NULL, 0", "ck_documentos_rag_uma_origem")]
    [InlineData("@chamado, @artigo, 0", "ck_documentos_rag_uma_origem")]
    [InlineData("@chamado, NULL, 1", "ck_documentos_rag_chunk_do_chamado")]
    [InlineData("NULL, @artigo, -1", "ck_documentos_rag_chunk_indice")]
    public async Task Inserir_OrigemOuChunkInvalidos_ERejeitadoPeloBanco(string valores, string constraint)
    {
        var (chamadoId, artigoId) = (await CriarChamadoAsync(), (await CriarArtigoAsync()).Id);

        var erro = await Should.ThrowAsync<PostgresException>(() => ExecutarAsync($"""
            INSERT INTO documentos_rag (id, chamado_id, artigo_id, chunk_indice, conteudo_mascarado, hash_conteudo)
            VALUES (gen_random_uuid(), {valores}, 'texto', '{_hash}')
            """, chamadoId, artigoId));

        erro.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        erro.ConstraintName.ShouldBe(constraint);
    }

    [Theory]
    [InlineData("embedding_modelo = 'fake-embedding-v1', indexado_em = now()")]
    [InlineData("embedding = array_fill(0.1, ARRAY[768])::vector, indexado_em = now()")]
    [InlineData("embedding = array_fill(0.1, ARRAY[768])::vector, embedding_modelo = 'fake-embedding-v1'")]
    public async Task Atualizar_IndexacaoIncompleta_ERejeitadaPeloBanco(string atribuicoes)
    {
        var artigo = await CriarArtigoAsync();
        await ExecutarAsync($"""
            INSERT INTO documentos_rag (id, artigo_id, chunk_indice, conteudo_mascarado, hash_conteudo)
            VALUES (gen_random_uuid(), @artigo, 0, 'texto', '{_hash}')
            """, Guid.Empty, artigo.Id);

        var erro = await Should.ThrowAsync<PostgresException>(() => ExecutarAsync(
            $"UPDATE documentos_rag SET {atribuicoes} WHERE artigo_id = @artigo", Guid.Empty, artigo.Id));

        erro.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        erro.ConstraintName.ShouldBe("ck_documentos_rag_indexacao_coerente");
    }

    [Fact]
    public async Task Inserir_VetorComOutraDimensao_ERejeitadoPeloBanco()
    {
        var artigo = await CriarArtigoAsync();

        var erro = await Should.ThrowAsync<PostgresException>(() => ExecutarAsync($"""
            INSERT INTO documentos_rag (id, artigo_id, chunk_indice, conteudo_mascarado, hash_conteudo, embedding,
                                        embedding_modelo, indexado_em)
            VALUES (gen_random_uuid(), @artigo, 0, 'texto', '{_hash}', array_fill(0.1, ARRAY[1536])::vector,
                    'outro-modelo', now())
            """, Guid.Empty, artigo.Id));

        erro.SqlState.ShouldBe(PostgresErrorCodes.DataException);
    }

    [Fact]
    public async Task Inserir_SegundoDocumentoDoMesmoChamado_ERecusadoMesmoComArtigoNulo()
    {
        var chamadoId = await CriarChamadoAsync();
        await ConsultarAsync(db =>
        {
            db.DocumentosRag.Add(DocumentoRag.DeChamado(chamadoId, "texto", _hash, null, _agora));
            return db.SaveChangesAsync(Ct);
        });

        var erro = await Should.ThrowAsync<DbUpdateException>(() => ConsultarAsync(db =>
        {
            db.DocumentosRag.Add(DocumentoRag.DeChamado(chamadoId, "outro texto", _hash, null, _agora));
            return db.SaveChangesAsync(Ct);
        }));

        var postgres = erro.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("ux_documentos_rag_origem_chunk");
    }

    [Fact]
    public async Task Apagar_OrigemDoDocumento_ApagaOsDocumentosEmCascata()
    {
        var chamadoId = await CriarChamadoAsync();
        var artigo = await CriarArtigoAsync();
        await ConsultarAsync(db =>
        {
            db.DocumentosRag.Add(DocumentoRag.DeChamado(chamadoId, "texto", _hash, null, _agora));
            db.DocumentosRag.Add(DocumentoRag.DeArtigo(artigo.Id, 0, "seção 1", _hash, null, _agora));
            db.DocumentosRag.Add(DocumentoRag.DeArtigo(artigo.Id, 1, "seção 2", _hash, null, _agora));
            return db.SaveChangesAsync(Ct);
        });

        await ConsultarAsync(db => db.Chamados.Where(c => c.Id == chamadoId).ExecuteDeleteAsync(Ct));
        await ConsultarAsync(db => db.Artigos.Where(a => a.Id == artigo.Id).ExecuteDeleteAsync(Ct));

        var restantes = await ConsultarAsync(db => db.DocumentosRag
            .CountAsync(d => d.ChamadoId == chamadoId || d.ArtigoId == artigo.Id, Ct));
        restantes.ShouldBe(0);
    }

    [Fact]
    public async Task Migration_Aplicada_CriaOsIndicesDaBuscaVetorialEDaFila()
    {
        var indices = await ConsultarAsync(db => db.Database
            .SqlQuery<string>($"""SELECT indexdef AS "Value" FROM pg_indexes WHERE tablename = 'documentos_rag'""")
            .ToListAsync(Ct));

        indices.ShouldContain(i => i.Contains("ix_documentos_rag_embedding_hnsw")
            && i.Contains("USING hnsw (embedding vector_cosine_ops)"));
        indices.ShouldContain(i => i.Contains("ix_documentos_rag_fila") && i.Contains("WHERE (embedding IS NULL)"));
        indices.ShouldContain(i => i.Contains("UNIQUE INDEX ux_documentos_rag_origem_chunk")
            && i.Contains("NULLS NOT DISTINCT"));
        indices.ShouldContain(i => i.Contains("ix_documentos_rag_artigo_id"));
    }

    // ---------- Apoio ----------

    private async Task<Guid> CriarChamadoAsync()
    {
        var chamado = Chamado.Abrir("Erro ao emitir boleto", "Desde ontem aparece erro 403 no módulo de boletos.",
            "Maria Exemplo", "maria@example.com", null, null, _agora);
        await ConsultarAsync(db =>
        {
            db.Chamados.Add(chamado);
            return db.SaveChangesAsync(Ct);
        });
        return chamado.Id;
    }

    private async Task<ArtigoConhecimento> CriarArtigoAsync()
    {
        var artigo = ArtigoConhecimento.Criar("Permissões do módulo financeiro",
            "## Sintoma\n\nErro 403 ao abrir boletos.\n\n## Solução\n\nLiberar o perfil financeiro.", 2, _agora);
        await ConsultarAsync(db =>
        {
            db.Artigos.Add(artigo);
            return db.SaveChangesAsync(Ct);
        });
        return artigo;
    }

    private Task<int> ExecutarAsync(string sql, Guid chamadoId, Guid artigoId) =>
        ConsultarAsync(db => db.Database.ExecuteSqlRawAsync(sql,
            [new NpgsqlParameter("chamado", chamadoId), new NpgsqlParameter("artigo", artigoId)], Ct));

    private async Task<T> ConsultarAsync<T>(Func<HelpDeskDbContext, Task<T>> consulta)
    {
        await using var servicos = banco.CriarServicos();
        await using var escopo = servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
    }
}
