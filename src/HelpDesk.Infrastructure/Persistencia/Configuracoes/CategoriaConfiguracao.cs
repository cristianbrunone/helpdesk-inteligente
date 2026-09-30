using HelpDesk.Domain.Categorias;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistencia.Configuracoes;

internal sealed class CategoriaConfiguracao : IEntityTypeConfiguration<Categoria>
{
    public void Configure(EntityTypeBuilder<Categoria> builder)
    {
        builder.ToTable("categorias");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityByDefaultColumn();

        builder.Property(c => c.Nome).HasMaxLength(Categoria.NomeTamanhoMaximo).IsRequired();
        builder.HasIndex(c => c.Nome).IsUnique();

        builder.Property(c => c.CriadoEm).HasDefaultValueSql("now()");
    }
}
