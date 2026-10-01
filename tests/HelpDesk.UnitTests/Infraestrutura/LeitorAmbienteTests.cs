using HelpDesk.Infrastructure.Configuracao;
using HelpDesk.Infrastructure.Ia;
using Microsoft.Extensions.AI;

namespace HelpDesk.UnitTests.Infraestrutura;

public sealed class LeitorAmbienteTests
{
    private static LeitorAmbiente Com(string chave, string? valor) =>
        new(c => c == chave ? valor : null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Booleano_AusenteOuVazio_UsaOPadrao(string? valor)
    {
        // "IA_TRIAGEM_HABILITADA=" no .env é "não configurado", não "false" (ADR-0023).
        Com("X", valor).Booleano("X", padrao: true).ShouldBeTrue();
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData(" False ", false)]
    [InlineData("TRUE", true)]
    public void Booleano_ValorValido_IgnoraCaixaEEspacos(string valor, bool esperado)
    {
        Com("X", valor).Booleano("X", padrao: !esperado).ShouldBe(esperado);
    }

    [Theory]
    [InlineData("talvez")]
    [InlineData("0")]
    [InlineData("sim")]
    public void Booleano_ValorInvalido_FalhaNaSubidaComMensagemClara(string valor)
    {
        Should.Throw<InvalidOperationException>(() => Com("IA_TRIAGEM_HABILITADA", valor)
                .Booleano("IA_TRIAGEM_HABILITADA", padrao: true))
            .Message.ShouldBe($"A variável IA_TRIAGEM_HABILITADA tem o valor '{valor}', mas deve ser 'true' ou 'false'.");
    }

    [Theory]
    [InlineData(null, 800)]
    [InlineData("", 800)]
    [InlineData("1500", 1500)]
    public void Inteiro_AusenteVazioOuValido_DevolveOValorCerto(string? valor, int esperado)
    {
        Com("X", valor).Inteiro("X", padrao: 800, minimo: 1, maximo: 10_000).ShouldBe(esperado);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10001")]
    [InlineData("1,5")]
    [InlineData("mil")]
    public void Inteiro_ForaDaFaixaOuNaoNumerico_FalhaNaSubida(string valor)
    {
        Should.Throw<InvalidOperationException>(() => Com("X", valor).Inteiro("X", 800, 1, 10_000))
            .Message.ShouldContain("um inteiro entre 1 e 10000");
    }

    [Fact]
    public void OpcoesIA_SemVariaveis_DeixaTudoHabilitado()
    {
        var opcoes = new LeitorAmbiente(_ => null).OpcoesIA();

        opcoes.TriagemHabilitada.ShouldBeTrue();
        opcoes.CopilotoHabilitado.ShouldBeTrue();
    }

    [Fact]
    public void OpcoesIA_TriagemDesligada_DesligaSoATriagem()
    {
        var opcoes = Com(LeitorAmbiente.IaTriagemHabilitada, "false").OpcoesIA();

        opcoes.TriagemHabilitada.ShouldBeFalse();
        opcoes.CopilotoHabilitado.ShouldBeTrue();
    }

    // ---------- Provedor de LLM (ADR-0005) ----------

    private const string ChaveFicticia = "chave-ficticia-de-teste-123";

    private static LeitorAmbiente ComVariaveis(params (string Chave, string Valor)[] variaveis) =>
        new(chave => variaveis.FirstOrDefault(v => v.Chave == chave).Valor);

    [Fact]
    public void OpcoesLlm_SemVariaveis_UsaOFakeEOsPadroesDaPoc()
    {
        var opcoes = new LeitorAmbiente(_ => null).OpcoesLlm();

        opcoes.Provedor.ShouldBe(TipoProvedorLlm.Fake);
        opcoes.ModeloEfetivo.ShouldBe("fake-triagem-v1");
        opcoes.Timeout.ShouldBe(TimeSpan.FromSeconds(60));
        opcoes.MaxRetries.ShouldBe(3);
        opcoes.MaxTokensSaidaTriagem.ShouldBe(800);
        opcoes.ModoFake.ShouldBe(ModoFake.Normal);
    }

    [Fact]
    public void OpcoesLlm_OpenAiCompativelCompleto_LeUrlChaveEModelo()
    {
        var opcoes = ComVariaveis(
            ("LLM_PROVIDER", "openai-compatible"),
            ("LLM_BASE_URL", "https://generativelanguage.googleapis.com/v1beta/openai/"),
            ("LLM_API_KEY", ChaveFicticia),
            ("LLM_CHAT_MODEL", "gemini-3.5-flash-lite"),
            ("LLM_MAX_RETRIES", "0")).OpcoesLlm();

        opcoes.Provedor.ShouldBe(TipoProvedorLlm.OpenAiCompativel);
        opcoes.BaseUrl!.Host.ShouldBe("generativelanguage.googleapis.com");
        opcoes.ModeloEfetivo.ShouldBe("gemini-3.5-flash-lite");
        opcoes.MaxRetries.ShouldBe(0);
    }

    [Fact]
    public void OpcoesLlm_ToString_NuncaMostraAChave()
    {
        var opcoes = ComVariaveis(("LLM_PROVIDER", "openai-compatible"), ("LLM_BASE_URL", "https://exemplo.local/v1/"),
            ("LLM_API_KEY", ChaveFicticia)).OpcoesLlm();

        opcoes.ToString().ShouldNotContain(ChaveFicticia);
        opcoes.ToString().ShouldContain("exemplo.local");
    }

    [Theory]
    [InlineData(null, "LLM_API_KEY", "LLM_PROVIDER=openai-compatible exige LLM_BASE_URL.")]
    [InlineData("https://exemplo.local/v1/", null, "LLM_PROVIDER=openai-compatible exige LLM_API_KEY no .env.")]
    [InlineData("ftp://exemplo.local/?key=segredo", "x", "A variável LLM_BASE_URL deve ser uma URL http(s) absoluta.")]
    public void OpcoesLlm_OpenAiCompativelIncompleto_FalhaSemEcoarSegredos(string? url, string? chave, string mensagem)
    {
        var leitor = ComVariaveis(("LLM_PROVIDER", "openai-compatible"), ("LLM_BASE_URL", url!), ("LLM_API_KEY", chave!));

        var erro = Should.Throw<InvalidOperationException>(() => leitor.OpcoesLlm());

        erro.Message.ShouldBe(mensagem);
        erro.Message.ShouldNotContain("segredo");
    }

    [Theory]
    [InlineData("LLM_PROVIDER", "gemini")]
    [InlineData("LLM_FAKE_MODO", "explodir")]
    [InlineData("LLM_TIMEOUT_SECONDS", "0")]
    [InlineData("TRIAGEM_MAX_TOKENS_SAIDA", "10")]
    public void OpcoesLlm_ValorInvalido_ImpedeASubida(string chave, string valor)
    {
        Should.Throw<InvalidOperationException>(() => ComVariaveis((chave, valor)).OpcoesLlm())
            .Message.ShouldStartWith($"A variável {chave}");
    }

    [Theory]
    [InlineData("lento", ModoFake.Lento)]
    [InlineData("JSON_INVALIDO", ModoFake.JsonInvalido)]
    [InlineData("categoria_inexistente", ModoFake.CategoriaInexistente)]
    [InlineData("rate_limit", ModoFake.RateLimit)]
    public void OpcoesLlm_ModoDoFake_EAceitoSemDiferenciarCaixa(string valor, ModoFake esperado)
    {
        ComVariaveis(("LLM_FAKE_MODO", valor)).OpcoesLlm().ModoFake.ShouldBe(esperado);
    }

    [Fact]
    public void FabricaClienteChat_OpenAiCompativel_CriaClienteNoEndpointEModeloConfigurados()
    {
        var opcoes = ComVariaveis(("LLM_PROVIDER", "openai-compatible"), ("LLM_BASE_URL", "https://exemplo.local/v1/"),
            ("LLM_API_KEY", ChaveFicticia), ("LLM_CHAT_MODEL", "modelo-x")).OpcoesLlm();

        using var cliente = FabricaClienteChat.Criar(opcoes);

        var metadados = cliente.GetService<ChatClientMetadata>()!;
        metadados.DefaultModelId.ShouldBe("modelo-x");
        metadados.ProviderUri!.Host.ShouldBe("exemplo.local");
    }
}
