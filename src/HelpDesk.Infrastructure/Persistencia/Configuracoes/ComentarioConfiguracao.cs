using HelpDesk.Domain.Chamados;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistencia.Configuracoes;

internal sealed class ComentarioConfiguracao : IEntityTypeConfiguration<Comentario>
{
    public void Configure(EntityTypeBuilder<Comentario> builder)
    {
        builder.ToTable("comentarios", tabela => tabela.HasCheckConstraint("ck_comentarios_texto_tamanho",
            $"char_length(texto) BETWEEN 1 AND {Comentario.TextoTamanhoMaximo}"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Autor).HasMaxLength(Comentario.AutorTamanhoMaximo).IsRequired();
        builder.Property(c => c.Texto).HasColumnType("text").IsRequired();

        // Índice 6 do modelo §5: FK + ordem cronológica de exibição no detalhe.
        builder.HasIndex(c => new { c.ChamadoId, c.CriadoEm }).HasDatabaseName("ix_comentarios_chamado_id_criado_em");
    }
}
