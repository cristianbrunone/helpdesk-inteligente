using HelpDesk.IntegrationTests.Infraestrutura;

namespace HelpDesk.IntegrationTests.Api;

public sealed class CorrelacaoTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Header = "X-Correlation-Id";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requisicao_SemHeaderDeCorrelacao_GeraIdEDevolveNaResposta()
    {
        using var resposta = await api.CreateClient().GetAsync("/openapi/v1.json", Ct);

        var id = resposta.Headers.GetValues(Header).Single();
        Guid.TryParse(id, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Requisicao_ComHeaderValido_DevolveOMesmoId()
    {
        using var resposta = await EnviarAsync("cliente-abc.123_x");

        resposta.Headers.GetValues(Header).Single().ShouldBe("cliente-abc.123_x");
    }

    [Theory]
    [InlineData("valor com espaço")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Requisicao_ComHeaderInseguro_IgnoraEGeraNovoId(string valorInseguro)
    {
        using var resposta = await EnviarAsync(valorInseguro);

        var id = resposta.Headers.GetValues(Header).Single();
        id.ShouldNotBe(valorInseguro);
        Guid.TryParse(id, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Requisicao_Qualquer_RegistraLogDaRequisicaoComCorrelationIdNoEscopo()
    {
        var id = $"log-{Guid.NewGuid():N}";

        using var resposta = await EnviarAsync(id);

        var registro = api.Logs.Registros
            .Where(r => r.Categoria.EndsWith("CorrelacaoMiddleware", StringComparison.Ordinal)
                && Equals(r.Escopo.GetValueOrDefault("CorrelationId"), id))
            .ShouldHaveSingleItem();
        registro.Mensagem.ShouldContain("GET");
        registro.Mensagem.ShouldContain("200");
    }

    private async Task<HttpResponseMessage> EnviarAsync(string correlationId)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        requisicao.Headers.TryAddWithoutValidation(Header, correlationId);
        return await api.CreateClient().SendAsync(requisicao, Ct);
    }
}
