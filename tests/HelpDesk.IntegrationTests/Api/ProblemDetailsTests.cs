using System.Text.Json;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Api;

public sealed class ProblemDetailsTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RotaInexistente_Requisitada_RetornaProblemDetails404NoFormatoDoContrato()
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/api/nao-existe");
        requisicao.Headers.Add("X-Correlation-Id", "teste-404");

        using var resposta = await api.CreateClient().SendAsync(requisicao, Ct);

        ((int)resposta.StatusCode).ShouldBe(StatusCodes.Status404NotFound);
        resposta.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        var raiz = json.RootElement;
        raiz.GetProperty("codigo").GetString().ShouldBe("nao_encontrado");
        raiz.GetProperty("type").GetString().ShouldBe("https://helpdesk.local/problemas/nao-encontrado");
        raiz.GetProperty("title").GetString().ShouldBe("Recurso não encontrado");
        raiz.GetProperty("instance").GetString().ShouldBe("/api/nao-existe");
        raiz.GetProperty("correlationId").GetString().ShouldBe("teste-404");
    }

    [Fact]
    public async Task ExcecaoInesperada_Tratada_Retorna500SemExporAMensagemDaExcecao()
    {
        using var escopo = api.CriarEscopo();
        var http = new DefaultHttpContext { RequestServices = escopo.ServiceProvider };
        http.Response.Body = new MemoryStream();
        var handler = escopo.ServiceProvider.GetServices<IExceptionHandler>().Single();

        var tratado = await handler.TryHandleAsync(
            http, new InvalidOperationException("detalhe interno: cpf 123.456.789-09"), Ct);

        tratado.ShouldBeTrue();
        http.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        http.Response.Body.Position = 0;
        var corpo = await new StreamReader(http.Response.Body).ReadToEndAsync(Ct);
        corpo.ShouldNotContain("123.456.789-09");
        corpo.ShouldNotContain("detalhe interno");
        using var json = JsonDocument.Parse(corpo);
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("erro_interno");
        json.RootElement.TryGetProperty("correlationId", out _).ShouldBeTrue();
    }
}
