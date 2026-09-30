using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HelpDesk.Infrastructure.Persistencia;

/// <summary>
/// Usada só pelo <c>dotnet ef</c> para gerar migrations. Gerar migration não conecta no banco,
/// então a connection string é fictícia e nenhuma configuração local é exigida.
/// </summary>
internal sealed class HelpDeskDbContextFactory : IDesignTimeDbContextFactory<HelpDeskDbContext>
{
    public HelpDeskDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HelpDeskDbContext>();
        ConfiguracaoBanco.Configurar(options, "Host=localhost;Database=helpdesk_design_time");
        return new HelpDeskDbContext(options.Options);
    }
}
