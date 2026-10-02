using HelpDesk.Domain.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Tabela <c>usuarios</c> (ADR-0026). O e-mail é guardado em minúsculas (o domínio normaliza e o <c>CHECK</c>
/// garante, mesmo para escrita fora da API), então o índice único comum já impede "Ana@" e "ana@" ao mesmo tempo.
/// </summary>
internal sealed class UsuarioConfiguracao : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuarios", tabela =>
        {
            tabela.HasCheckConstraint("ck_usuarios_email_minusculo", "email = lower(email)");
            tabela.HasCheckConstraint("ck_usuarios_email_formato", @"email ~* '^[^@\s]+@[^@\s]+\.[^@\s]+$'");
            tabela.HasCheckConstraint("ck_usuarios_nome_preenchido", "char_length(btrim(nome)) > 0");
        });

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Nome).HasMaxLength(Usuario.NomeTamanhoMaximo).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(Usuario.EmailTamanhoMaximo).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.SenhaHash).HasMaxLength(200).IsRequired();
    }
}
