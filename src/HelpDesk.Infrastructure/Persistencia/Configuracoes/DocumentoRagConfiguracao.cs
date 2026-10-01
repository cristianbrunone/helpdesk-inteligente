using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Conhecimento;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>documentos_rag</c> (modelo §2, §4 e §5; ADR-0007, ADR-0011): uma linha por chamado resolvido ou por
/// chunk de artigo, com o vetor de 768 dimensões e os metadados que o reconciliador compara (hash e modelo).
/// </summary>
internal sealed class DocumentoRagConfiguracao : IEntityTypeConfiguration<DocumentoRag>
{
    public const string IndiceBuscaVetorial = "ix_documentos_rag_embedding_hnsw";

    public void Configure(EntityTypeBuilder<DocumentoRag> builder)
    {
        builder.ToTable("documentos_rag", tabela =>
        {
            tabela.HasCheckConstraint("ck_documentos_rag_uma_origem", "num_nonnulls(chamado_id, artigo_id) = 1");
            // Vetor, modelo e data de indexação andam juntos: ou o documento está indexado, ou está na fila.
            tabela.HasCheckConstraint("ck_documentos_rag_indexacao_coerente",
                "(embedding IS NULL) = (embedding_modelo IS NULL) AND (embedding IS NULL) = (indexado_em IS NULL)");
            // O chamado vira um documento só (ADR-0011); só artigos têm vários chunks.
            tabela.HasCheckConstraint("ck_documentos_rag_chunk_do_chamado", "chamado_id IS NULL OR chunk_indice = 0");
            tabela.HasCheckConstraint("ck_documentos_rag_chunk_indice", "chunk_indice >= 0");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.ConteudoMascarado).HasColumnType("text").IsRequired();
        builder.Property(d => d.HashConteudo).HasColumnType("char(64)").IsRequired();
        builder.Property(d => d.Embedding).HasColumnType($"vector({DocumentoRag.Dimensoes})");
        builder.Property(d => d.EmbeddingModelo).HasMaxLength(100);
        // Sem valor conhecido (escrita fora do reconciliador), o documento é conferido de novo na próxima passada.
        builder.Property(d => d.OrigemAtualizadaEm).HasDefaultValueSql("'-infinity'");

        builder.HasOne<Chamado>()
            .WithMany()
            .HasForeignKey(d => d.ChamadoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ArtigoConhecimento>()
            .WithMany()
            .HasForeignKey(d => d.ArtigoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Uma origem não tem dois documentos no mesmo chunk. NULLS NOT DISTINCT: sem isso, (chamado, NULL, 0)
        // repetido passaria, porque NULL nunca é igual a NULL. Começa por chamado_id, então também cobre essa FK.
        builder.HasIndex(d => new { d.ChamadoId, d.ArtigoId, d.ChunkIndice })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_documentos_rag_origem_chunk");

        // Índice 11: busca semântica por cosseno (ADR-0007). HNSW dispensa treino e aceita inserts incrementais.
        builder.HasIndex(d => d.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasDatabaseName(IndiceBuscaVetorial);

        // Índice 12: a fila de indexação. Só os documentos ainda sem vetor.
        builder.HasIndex(d => d.IndexadoEm)
            .HasFilter("embedding IS NULL")
            .HasDatabaseName("ix_documentos_rag_fila");

        // Índice 13: o reconciliador compara artigo × documentos, e o ON DELETE CASCADE do artigo usa este índice.
        builder.HasIndex(d => d.ArtigoId).HasDatabaseName("ix_documentos_rag_artigo_id");
    }
}
