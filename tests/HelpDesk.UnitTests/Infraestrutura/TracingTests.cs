using HelpDesk.Infrastructure.Configuracao;
using HelpDesk.Infrastructure.Observabilidade;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace HelpDesk.UnitTests.Infraestrutura;

public sealed class TracingTests
{
    [Fact]
    public void AdicionarTracing_SemEndpoint_NaoRegistraNada()
    {
        var services = new ServiceCollection();

        services.AdicionarTracing("teste", endpointOtlp: null);

        using var provedor = services.BuildServiceProvider();
        provedor.GetService<TracerProvider>().ShouldBeNull();
    }

    [Fact]
    public void AdicionarTracing_ComEndpoint_RegistraOTracerProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AdicionarTracing("teste", new Uri("http://localhost:4317"));

        using var provedor = services.BuildServiceProvider();
        provedor.GetService<TracerProvider>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("http://aspire-dashboard:18889", "aspire-dashboard")]
    public void EndpointOtlp_VazioOuValido_LidoComoUri(string? valor, string? host)
    {
        new LeitorAmbiente(_ => valor).EndpointOtlp()?.Host.ShouldBe(host);
    }

    [Fact]
    public void EndpointOtlp_Invalido_ImpedeASubida()
    {
        Should.Throw<InvalidOperationException>(() => new LeitorAmbiente(_ => "aspire-dashboard:18889").EndpointOtlp());
    }
}
