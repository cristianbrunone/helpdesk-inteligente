using HelpDesk.Domain.Categorias;
using HelpDesk.Domain.Chamados;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>chamados</c> (modelo §2, §4 e §5). O índice de busca por texto (#5) é de expressão e fica em SQL
/// na migration, junto com a função <c>f_unaccent</c> (ADR-0008).
/// </summary>
internal sealed class ChamadoConfiguracao : IEntityTypeConfiguration<Chamado>
{
    public void Configure(EntityTypeBuilder<Chamado> builder)
    {
        builder.ToTable("chamados", tabela =>
        {
            tabela.HasCheckConstraint("ck_chamados_titulo_tamanho",
                $"char_length(titulo) BETWEEN {Chamado.TituloTamanhoMinimo} AND {Chamado.TituloTamanhoMaximo}");
            tabela.HasCheckConstraint("ck_chamados_descricao_tamanho",
                $"char_length(descricao) BETWEEN {Chamado.DescricaoTamanhoMinimo} AND {Chamado.DescricaoTamanhoMaximo}");
            // A validação completa do e-mail fica na API; o banco barra o grosseiramente inválido.
            tabela.HasCheckConstraint("ck_chamados_solicitante_email_formato",
                @"solicitante_email ~* '^[^@\s]+@[^@\s]+\.[^@\s]+$'");
            // RN-03: resolvido_em existe exatamente nos status que passaram pela resolução.
            tabela.HasCheckConstraint("ck_chamados_resolvido_em_coerente",
                "(resolvido_em IS NOT NULL) = (status IN ('resolvido', 'fechado'))");
            tabela.HasCheckConstraint("ck_chamados_resolvido_em_apos_criacao",
                "resolvido_em IS NULL OR resolvido_em >= criado_em");
            // RN-05 garantida até para escrita fora da API.
            tabela.HasCheckConstraint("ck_chamados_critica_nao_cancelada",
                "NOT (prioridade = 'critica' AND status = 'cancelado')");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Numero).UseIdentityAlwaysColumn();
        builder.HasIndex(c => c.Numero).IsUnique().HasDatabaseName("ux_chamados_numero");

        builder.Property(c => c.Titulo).HasMaxLength(Chamado.TituloTamanhoMaximo).IsRequired();
        builder.Property(c => c.Descricao).HasColumnType("text").IsRequired();
        builder.Property(c => c.SolicitanteNome).HasMaxLength(Chamado.SolicitanteNomeTamanhoMaximo).IsRequired();
        builder.Property(c => c.SolicitanteEmail).HasMaxLength(Chamado.SolicitanteEmailTamanhoMaximo).IsRequired();

        builder.Property<uint>(HelpDeskDbContext.VersaoChamado).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

        builder.HasOne<Categoria>()
            .WithMany()
            .HasForeignKey(c => c.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Comentarios)
            .WithOne()
            .HasForeignKey(c => c.ChamadoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Historico)
            .WithOne()
            .HasForeignKey(h => h.ChamadoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Calculados pelo domínio, não persistidos.
        builder.Ignore(c => c.Finalizado);
        builder.Ignore(c => c.PodeComentar);
        builder.Ignore(c => c.TransicoesPermitidas);

        // Índices 1 a 4 do modelo §5. O #4 também cobre a FK de categoria (o PostgreSQL não indexa FKs sozinho).
        builder.HasIndex(c => new { c.CriadoEm, c.Id })
            .IsDescending(true, true)
            .HasDatabaseName("ix_chamados_criado_em_id");
        builder.HasIndex(c => new { c.Status, c.CriadoEm })
            .IsDescending(false, true)
            .HasDatabaseName("ix_chamados_status_criado_em");
        builder.HasIndex(c => new { c.Prioridade, c.CriadoEm })
            .IsDescending(true, true)
            .HasDatabaseName("ix_chamados_prioridade_criado_em");
        builder.HasIndex(c => new { c.CategoriaId, c.CriadoEm })
            .IsDescending(false, true)
            .HasDatabaseName("ix_chamados_categoria_id_criado_em");
    }
}
