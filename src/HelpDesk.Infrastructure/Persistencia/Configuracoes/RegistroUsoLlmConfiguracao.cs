using HelpDesk.Domain.Triagem;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistencia.Configuracoes;

internal sealed class RegistroUsoLlmConfiguracao : IEntityTypeConfiguration<RegistroUsoLlm>
{
    public void Configure(EntityTypeBuilder<RegistroUsoLlm> builder)
    {
        builder.ToTable("uso_llm", tabela =>
        {
            tabela.HasCheckConstraint("ck_uso_llm_operacao",
                $"operacao IN ('{RegistroUsoLlm.OperacaoTriagem}', '{RegistroUsoLlm.OperacaoCopiloto}', " +
                $"'{RegistroUsoLlm.OperacaoEmbedding}')");
            tabela.HasCheckConstraint("ck_uso_llm_erro_coerente", "sucesso = (erro_tipo IS NULL)");
            tabela.HasCheckConstraint("ck_uso_llm_valores_positivos",
                "latencia_ms >= 0 AND (tokens_entrada IS NULL OR tokens_entrada >= 0) " +
                "AND (tokens_saida IS NULL OR tokens_saida >= 0)");
        });

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).UseIdentityAlwaysColumn();

        builder.Property(u => u.Operacao).HasMaxLength(20).IsRequired();
        builder.Property(u => u.Provedor).HasMaxLength(40).IsRequired();
        builder.Property(u => u.Modelo).HasMaxLength(100).IsRequired();
        builder.Property(u => u.ErroTipo).HasMaxLength(40);

        // O consumo sobrevive à triagem (livro-razão): apagar a triagem só desliga a referência.
        builder.HasOne<TriagemIA>()
            .WithMany()
            .HasForeignKey(u => u.TriagemId)
            .OnDelete(DeleteBehavior.SetNull);

        // Índice 14: as consultas de consumo são sempre por janela de tempo.
        builder.HasIndex(u => u.CriadoEm).HasDatabaseName("ix_uso_llm_criado_em");
    }
}
