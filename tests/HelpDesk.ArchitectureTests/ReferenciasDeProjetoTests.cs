using System.Xml.Linq;

namespace HelpDesk.ArchitectureTests;

/// <summary>
/// Regra de dependência no nível dos .csproj. O compilador descarta referências sem uso, então o teste
/// de tipos sozinho não enxergaria uma referência proibida que ainda não foi usada no código.
/// </summary>
public sealed class ReferenciasDeProjetoTests
{
    [Fact]
    public void Domain_Csproj_NaoTemNenhumaReferencia()
    {
        var projeto = Csproj.Ler(Camadas.Domain);

        projeto.ReferenciasDeProjeto.ShouldBeEmpty();
        projeto.Pacotes.ShouldBeEmpty();
        projeto.Frameworks.ShouldBeEmpty();
    }

    [Fact]
    public void Application_Csproj_ReferenciaSoDomainESemFrameworksDeInfraestrutura()
    {
        var projeto = Csproj.Ler(Camadas.Application);

        projeto.ReferenciasDeProjeto.ShouldBe([Camadas.Domain]);
        projeto.Frameworks.ShouldBeEmpty();
        projeto.Pacotes
            .Where(p => Camadas.FrameworksDeInfraestrutura.Any(f => p.StartsWith(f, StringComparison.Ordinal)))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Infrastructure_Csproj_NaoReferenciaHosts()
    {
        var projeto = Csproj.Ler(Camadas.Infrastructure);

        projeto.ReferenciasDeProjeto.ShouldNotContain(r => Camadas.Hosts.Contains(r));
    }

    private sealed record Csproj(string[] ReferenciasDeProjeto, string[] Pacotes, string[] Frameworks)
    {
        public static Csproj Ler(string nome)
        {
            var xml = XDocument.Load(Path.Combine(RaizDoRepositorio(), "src", nome, $"{nome}.csproj"));

            string[] Valores(string elemento) =>
                [.. xml.Descendants(elemento).Select(e => (string?)e.Attribute("Include") ?? "")];

            return new Csproj(
                [.. Valores("ProjectReference").Select(Path.GetFileNameWithoutExtension).OfType<string>()],
                Valores("PackageReference"),
                Valores("FrameworkReference"));
        }

        private static string RaizDoRepositorio()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HelpDesk.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("HelpDesk.slnx não encontrado.");
        }
    }
}
