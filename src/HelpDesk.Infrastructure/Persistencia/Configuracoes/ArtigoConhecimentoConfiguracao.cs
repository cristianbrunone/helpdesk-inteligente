using HelpDesk.Domain.Categorias;
using HelpDesk.Domain.Conhecimento;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistencia.Configuracoes;

/// <summary>Tabela <c>artigos_conhecimento</c> (modelo §2): base de conhecimento do RAG (RF-30).</summary>
internal sealed class ArtigoConhecimentoConfiguracao : IEntityTypeConfiguration<ArtigoConhecimento>
{
    public void Configure(EntityTypeBuilder<ArtigoConhecimento> builder)
    {
        builder.ToTable("artigos_conhecimento", tabela =>
        {
            tabela.HasCheckConstraint("ck_artigos_conhecimento_titulo_preenchido", "char_length(btrim(titulo)) > 0");
            tabela.HasCheckConstraint("ck_artigos_conhecimento_conteudo_preenchido", "char_length(btrim(conteudo)) > 0");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Titulo).HasMaxLength(ArtigoConhecimento.TituloTamanhoMaximo).IsRequired();
        builder.Property(a => a.Conteudo).HasColumnType("text").IsRequired();

        // RESTRICT como em chamados: não se apaga uma categoria em uso. O índice da FK vem por convenção.
        builder.HasOne<Categoria>()
            .WithMany()
            .HasForeignKey(a => a.CategoriaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
