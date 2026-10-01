using HelpDesk.Domain.Categorias;
using HelpDesk.Domain.Chamados;
using HelpDesk.Domain.Triagem;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>triagens_ia</c> (modelo §2, §4 e §5). É também a fila do Worker (ADR-0010): os índices 9 e 10 são
/// parciais sobre <c>status = 'pendente'</c>, então ficam pequenos mesmo com milhões de triagens históricas.
/// </summary>
internal sealed class TriagemIAConfiguracao : IEntityTypeConfiguration<TriagemIA>
{
    public const string FiltroPendente = "status = 'pendente'";

    public void Configure(EntityTypeBuilder<TriagemIA> builder)
    {
        builder.ToTable("triagens_ia", tabela =>
        {
            tabela.HasCheckConstraint("ck_triagens_ia_confianca_faixa", "confianca IS NULL OR confianca BETWEEN 0 AND 1");
            tabela.HasCheckConstraint("ck_triagens_ia_resumo_tamanho",
                $"resumo IS NULL OR char_length(resumo) <= {TriagemIA.ResumoTamanhoMaximo}");
            // Sugestão completa sempre que a triagem é (ou foi) válida.
            tabela.HasCheckConstraint("ck_triagens_ia_sugestao_completa",
                "status NOT IN ('concluida', 'aceita', 'rejeitada') OR (categoria_sugerida_id IS NOT NULL " +
                "AND prioridade_sugerida IS NOT NULL AND resumo IS NOT NULL AND resposta_sugerida IS NOT NULL " +
                "AND confianca IS NOT NULL)");
            tabela.HasCheckConstraint("ck_triagens_ia_falha_com_motivo", "status <> 'falhou' OR erro_motivo IS NOT NULL");
            tabela.HasCheckConstraint("ck_triagens_ia_decisao_coerente",
                "(status IN ('aceita', 'rejeitada')) = (decidida_em IS NOT NULL)");
            // Pendente ainda não sabe com qual provedor/modelo/prompt será processada; depois, é obrigatório.
            tabela.HasCheckConstraint("ck_triagens_ia_execucao_registrada",
                "status = 'pendente' OR (provedor IS NOT NULL AND modelo IS NOT NULL AND prompt_versao IS NOT NULL)");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // text + CHECK (e não varchar(200)): o limite fica numa regra nomeada e testável, como no modelo §4.
        builder.Property(t => t.Resumo).HasColumnType("text");
        builder.Property(t => t.RespostaSugerida).HasColumnType("text");
        builder.Property(t => t.Confianca).HasPrecision(4, 3);
        builder.Property(t => t.Provedor).HasMaxLength(40);
        builder.Property(t => t.Modelo).HasMaxLength(100);
        builder.Property(t => t.PromptVersao).HasMaxLength(40);
        builder.Property(t => t.ErroMotivo).HasColumnType("text");
        builder.Property(t => t.TraceParent).HasMaxLength(55);
        builder.Property(t => t.DecididaPor).HasMaxLength(TriagemIA.DecididaPorTamanhoMaximo);
        builder.Property(t => t.MotivoRejeicao).HasMaxLength(TriagemIA.MotivoRejeicaoTamanhoMaximo);

        builder.HasOne<Chamado>()
            .WithMany()
            .HasForeignKey(t => t.ChamadoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Categoria>()
            .WithMany()
            .HasForeignKey(t => t.CategoriaSugeridaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índice 8: triagem vigente = a mais recente do chamado (P-04). Também cobre a FK.
        builder.HasIndex(t => new { t.ChamadoId, t.CriadoEm })
            .IsDescending(false, true)
            .HasDatabaseName("ix_triagens_ia_chamado_id_criado_em");

        // Índice 9: a fila. Só as pendentes, na ordem em que podem ser tentadas.
        builder.HasIndex(t => t.ProximaTentativaEm)
            .HasFilter(FiltroPendente)
            .HasDatabaseName("ix_triagens_ia_fila");

        // Índice 10: no máximo uma pendente por chamado ("Refazer" durante uma pendente → 409).
        builder.HasIndex(t => t.ChamadoId)
            .IsUnique()
            .HasFilter(FiltroPendente)
            .HasDatabaseName("ux_triagens_ia_uma_pendente_por_chamado");
    }
}
