using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDesk.Api.Autenticacao;
using HelpDesk.Application.Autenticacao;
using HelpDesk.Domain.Usuarios;
using HelpDesk.Infrastructure.Persistencia.Seed;
using HelpDesk.Infrastructure.Seguranca;
using HelpDesk.IntegrationTests.Infraestrutura;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.IntegrationTests.Api;

/// <summary>Login, sessão e saída (ADR-0026, contrato §3 "Autenticação"), com os usuários do seed.</summary>
public sealed class AutenticacaoTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private const string Senha = GeradorSeedUsuarios.SenhaDemonstracao;
    private static readonly UsuarioDemonstracao _ana = GeradorSeedUsuarios.AnaAtendente;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_CredenciaisCertas_DevolveOUsuarioSemTokenNoCorpo()
    {
        using var resposta = await EntrarAsync("  Ana.Suporte@Example.com ", Senha);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        using var json = JsonDocument.Parse(corpo);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["id", "nome", "email", "perfil"]);
        json.RootElement.GetProperty("nome").GetString().ShouldBe(_ana.Nome);
        json.RootElement.GetProperty("email").GetString().ShouldBe(_ana.Email);
        json.RootElement.GetProperty("perfil").GetString().ShouldBe("Atendente");
        corpo.ShouldNotContain(Cookie(resposta).Valor); // o JavaScript nunca vê o token
    }

    [Fact]
    public async Task Login_CredenciaisCertas_GravaOCookieHttpOnlySecureSameSiteStrictPorOitoHoras()
    {
        using var resposta = await EntrarAsync(_ana.Email, Senha);

        var cookie = Cookie(resposta);
        cookie.Atributos.ShouldContain("httponly");
        cookie.Atributos.ShouldContain("secure");
        cookie.Atributos.ShouldContain("samesite=strict");
        cookie.Atributos.ShouldContain("path=/");
        var expira = DateTimeOffset.Parse(cookie.Atributos.Single(a => a.StartsWith("expires=", StringComparison.Ordinal))[8..],
            System.Globalization.CultureInfo.InvariantCulture);
        (expira - DateTimeOffset.UtcNow).ShouldBeInRange(TimeSpan.FromHours(7.9), TimeSpan.FromHours(8.1));
    }

    [Theory]
    [InlineData("ana.suporte@example.com", "senha-errada")]
    [InlineData("ninguem@example.com", Senha)]
    public async Task Login_SenhaErradaOuEmailInexistente_401ComAMesmaMensagemESemCookie(string email, string senha)
    {
        using var resposta = await EntrarAsync(email, senha);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        resposta.Headers.Contains("Set-Cookie").ShouldBeFalse();
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("nao_autenticado");
        json.RootElement.GetProperty("detail").GetString().ShouldBe(NaoAutenticadoException.MensagemPadrao);
    }

    [Fact]
    public async Task Login_SemEmailNemSenha_422ComOsDoisCampos()
    {
        using var resposta = await api.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = " " }, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        var erros = json.RootElement.GetProperty("errors");
        erros.TryGetProperty("email", out _).ShouldBeTrue();
        erros.TryGetProperty("senha", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Eu_ComOCookieDoLogin_DevolveQuemEstaNaSessao()
    {
        using var login = await EntrarAsync(GeradorSeedUsuarios.MarinaSolicitante.Email, Senha);
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Add("Cookie", $"{ConfiguracaoAutenticacao.NomeCookie}={Cookie(login).Valor}");

        using var resposta = await cliente.GetAsync("/api/auth/eu", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("nome").GetString().ShouldBe("Marina Costa");
        json.RootElement.GetProperty("perfil").GetString().ShouldBe("Solicitante");
    }

    [Fact]
    public async Task Eu_ComOHeaderBearer_TambemAceita()
    {
        using var login = await EntrarAsync(_ana.Email, Senha);
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Cookie(login).Valor);

        using var resposta = await cliente.GetAsync("/api/auth/eu", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Eu_SemSessao_401NoFormatoDoContrato()
    {
        using var resposta = await api.CreateClient().GetAsync("/api/auth/eu", Ct);

        await DeveSerNaoAutenticadoAsync(resposta);
    }

    [Fact]
    public async Task Eu_TokenAdulterado_401()
    {
        using var login = await EntrarAsync(_ana.Email, Senha);
        var partes = Cookie(login).Valor.Split('.');
        // Troca o perfil no corpo do token, sem reassinar: a assinatura não confere mais.
        var corpo = System.Text.Encoding.UTF8.GetString(Base64Url(partes[1])).Replace("Atendente", "Solicitante");
        var adulterado = $"{partes[0]}.{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(corpo)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.{partes[2]}";

        await DeveSerNaoAutenticadoAsync(await ComBearerAsync(adulterado));
    }

    [Fact]
    public async Task Eu_TokenExpirado_401()
    {
        var opcoes = api.Services.GetRequiredService<OpcoesSessao>();
        var emitidoOntem = new EmissorToken(opcoes, new RelogioFixo(DateTimeOffset.UtcNow.AddDays(-1)))
            .Emitir(new UsuarioAutenticado(Guid.CreateVersion7(), _ana.Nome, _ana.Email, PerfilUsuario.Atendente));

        await DeveSerNaoAutenticadoAsync(await ComBearerAsync(emitidoOntem.Token));
    }

    [Fact]
    public async Task Eu_TokenAssinadoComOutraChave_401()
    {
        var outra = new OpcoesSessao(new byte[32], ChaveGerada: true, CookieSeguro: true, TimeSpan.FromHours(8));
        var forjado = new EmissorToken(outra, TimeProvider.System)
            .Emitir(new UsuarioAutenticado(Guid.CreateVersion7(), "Intruso", "x@example.com", PerfilUsuario.Atendente));

        await DeveSerNaoAutenticadoAsync(await ComBearerAsync(forjado.Token));
    }

    [Fact]
    public async Task Sair_ApagaOCookie()
    {
        using var resposta = await api.CreateClient().PostAsync("/api/auth/sair", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cookie = Cookie(resposta);
        cookie.Valor.ShouldBeEmpty();
        cookie.Atributos.ShouldContain(a => a.StartsWith("expires=Thu, 01 Jan 1970", StringComparison.Ordinal));
    }

    // ---------- Apoio ----------

    private Task<HttpResponseMessage> EntrarAsync(string email, string senha) =>
        api.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, senha }, Ct);

    private Task<HttpResponseMessage> ComBearerAsync(string token)
    {
        var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente.GetAsync("/api/auth/eu", Ct);
    }

    private static async Task DeveSerNaoAutenticadoAsync(HttpResponseMessage resposta)
    {
        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        resposta.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("codigo").GetString().ShouldBe("nao_autenticado");
        json.RootElement.TryGetProperty("correlationId", out _).ShouldBeTrue();
    }

    /// <summary>O cookie da sessão no Set-Cookie: o valor e os atributos em minúsculas.</summary>
    private static (string Valor, string[] Atributos) Cookie(HttpResponseMessage resposta)
    {
        var linha = resposta.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(ConfiguracaoAutenticacao.NomeCookie + "=", StringComparison.Ordinal));
        var partes = linha.Split(';', StringSplitOptions.TrimEntries);
        var atributos = partes.Skip(1)
            .Select(a => a.StartsWith("expires=", StringComparison.OrdinalIgnoreCase) ? "expires=" + a[8..] : a.ToLowerInvariant())
            .ToArray();
        return (partes[0][(ConfiguracaoAutenticacao.NomeCookie.Length + 1)..], atributos);
    }

    private static byte[] Base64Url(string texto)
    {
        var base64 = texto.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
