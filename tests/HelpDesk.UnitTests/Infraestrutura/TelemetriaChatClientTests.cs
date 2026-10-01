using System.ClientModel;
using HelpDesk.Infrastructure.Ia;

namespace HelpDesk.UnitTests.Infraestrutura;

public sealed class TelemetriaChatClientTests
{
    [Fact]
    public void TipoDoErro_FalhaJaClassificada_UsaOTipoDela()
    {
        TelemetriaChatClient.TipoDoErro(new ProvedorIndisponivelException("rate_limit", "429")).ShouldBe("rate_limit");
    }

    [Fact]
    public void TipoDoErro_TentativaCortadaPeloTimeout_ETimeout()
    {
        TelemetriaChatClient.TipoDoErro(new TaskCanceledException()).ShouldBe("timeout");
    }

    [Fact]
    public void TipoDoErro_FalhaDeRede_EIndisponivel()
    {
        TelemetriaChatClient.TipoDoErro(new HttpRequestException("falha")).ShouldBe("indisponivel");
    }

    [Fact]
    public void TipoDoErro_ExcecaoDesconhecida_NaoUsaAMensagem()
    {
        // A mensagem de uma exceção pode carregar conteúdo; o registro guarda só a categoria.
        TelemetriaChatClient.TipoDoErro(new InvalidOperationException("texto do usuário: 529.982.247-25"))
            .ShouldBe("erro");
    }

    [Fact]
    public void TipoDoErro_ClientResultSemRespostaHttp_EIndisponivel()
    {
        // Falha de transporte (TLS, conexão, DNS): não houve resposta do provedor.
        TelemetriaChatClient.TipoDoErro(new ClientResultException("sem resposta")).ShouldBe("indisponivel");
    }
}
