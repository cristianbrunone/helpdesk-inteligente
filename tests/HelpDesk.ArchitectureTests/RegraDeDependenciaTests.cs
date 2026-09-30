using NetArchTest.Rules;
using ResultadoArquitetura = NetArchTest.Rules.TestResult;

namespace HelpDesk.ArchitectureTests;

/// <summary>
/// Regra de dependência no nível dos tipos: Domain ← Application ← Infrastructure; hosts compõem via DI (ADR-0002).
/// </summary>
public sealed class RegraDeDependenciaTests
{
    [Fact]
    public void Domain_TiposDoAssembly_NaoDependemDeOutrasCamadasNemDeFrameworks()
    {
        string[] proibidas =
        [
            Camadas.Application, Camadas.Infrastructure, .. Camadas.Hosts,
            .. Camadas.FrameworksDeInfraestrutura, "Microsoft.Extensions",
        ];

        var resultado = Types.InAssembly(Camadas.Carregar(Camadas.Domain))
            .ShouldNot().HaveDependencyOnAny(proibidas)
            .GetResult();

        DeveSerValido(resultado);
    }

    [Fact]
    public void Application_TiposDoAssembly_NaoDependemDeInfrastructureHostsNemFrameworksDeInfraestrutura()
    {
        string[] proibidas = [Camadas.Infrastructure, .. Camadas.Hosts, .. Camadas.FrameworksDeInfraestrutura];

        var resultado = Types.InAssembly(Camadas.Carregar(Camadas.Application))
            .ShouldNot().HaveDependencyOnAny(proibidas)
            .GetResult();

        DeveSerValido(resultado);
    }

    [Fact]
    public void Infrastructure_TiposDoAssembly_NaoDependemDosHosts()
    {
        var resultado = Types.InAssembly(Camadas.Carregar(Camadas.Infrastructure))
            .ShouldNot().HaveDependencyOnAny(Camadas.Hosts)
            .GetResult();

        DeveSerValido(resultado);
    }

    private static void DeveSerValido(ResultadoArquitetura resultado) =>
        resultado.IsSuccessful.ShouldBeTrue(
            $"Tipos que violam a regra: {string.Join(", ", resultado.FailingTypeNames ?? [])}");
}
