using HelpDesk.Domain.Chamados;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HelpDesk.Infrastructure.Persistencia.Configuracoes;

internal sealed class HistoricoStatusConfiguracao : IEntityTypeConfiguration<HistoricoStatus>
{
    public void Configure(EntityTypeBuilder<HistoricoStatus> builder)
    {
        // RN-06: mudar para o mesmo status não é transição. NULL (criação) é distinto de qualquer status.
        builder.ToTable("historico_status", tabela => tabela.HasCheckConstraint(
            "ck_historico_status_muda_status", "status_anterior IS DISTINCT FROM status_novo"));

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).UseIdentityAlwaysColumn();

        builder.Property(h => h.AlteradoPor).HasMaxLength(HistoricoStatus.AlteradoPorTamanhoMaximo).IsRequired();

        // Índice 7 do modelo §5.
        builder.HasIndex(h => new { h.ChamadoId, h.AlteradoEm })
            .HasDatabaseName("ix_historico_status_chamado_id_alterado_em");
    }
}
