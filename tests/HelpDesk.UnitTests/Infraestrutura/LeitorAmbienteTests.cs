using HelpDesk.Application.Conhecimento;
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
        opcoes.MaxTokensSaidaCopiloto.ShouldBe(800);
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
    public void OpcoesSessao_SemChave_GeraUmaAleatoriaDe32BytesECookieSeguro()
    {
        var opcoes = ComVariaveis().OpcoesSessao();

        opcoes.ChaveGerada.ShouldBeTrue();
        opcoes.Chave.Length.ShouldBe(32);
        opcoes.CookieSeguro.ShouldBeTrue();
        opcoes.Validade.ShouldBe(TimeSpan.FromHours(8));
        ComVariaveis().OpcoesSessao().Chave.ShouldNotBe(opcoes.Chave); // muda a cada subida
    }

    [Fact]
    public void OpcoesSessao_ComChave_UsaAChaveEAceitaCookieSemHttps()
    {
        var chave = new string('k', 40);

        var opcoes = ComVariaveis(("JWT_CHAVE", chave), ("SESSAO_COOKIE_SEGURO", "false")).OpcoesSessao();

        opcoes.ChaveGerada.ShouldBeFalse();
        opcoes.Chave.ShouldBe(System.Text.Encoding.UTF8.GetBytes(chave));
        opcoes.CookieSeguro.ShouldBeFalse();
        opcoes.ToString().ShouldNotContain(chave);
    }

    [Fact]
    public void OpcoesSessao_ChaveCurta_ImpedeASubidaSemEcoarOValor()
    {
        var erro = Should.Throw<InvalidOperationException>(() =>
            ComVariaveis(("JWT_CHAVE", "segredo-curto")).OpcoesSessao());

        erro.Message.ShouldStartWith("A variável JWT_CHAVE deve ter pelo menos 32 bytes");
        erro.Message.ShouldNotContain("segredo-curto");
    }

    [Fact]
    public void OpcoesLlm_OrcamentoDoCopiloto_LidoDoAmbiente() =>
        ComVariaveis(("COPILOTO_MAX_TOKENS_SAIDA", "300")).OpcoesLlm().MaxTokensSaidaCopiloto.ShouldBe(300);

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
    [InlineData("COPILOTO_MAX_TOKENS_SAIDA", "9000")]
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
    [InlineData("vaza_dados", ModoFake.VazaDados)]
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

    [Theory]
    [InlineData(null)]
    [InlineData("768")]
    public void OpcoesLlm_DimensaoAusenteOu768_SobeComOModeloDeEmbeddingPadrao(string? dimensoes)
    {
        var opcoes = ComVariaveis(("EMBEDDING_DIMENSIONS", dimensoes ?? "")).OpcoesLlm();

        opcoes.ModeloEmbedding.ShouldBe("gemini-embedding-001");
        opcoes.ModeloEmbeddingEfetivo.ShouldBe(OpcoesLlm.ModeloEmbeddingFake);
    }

    [Theory]
    [InlineData("1536")]
    [InlineData("abc")]
    public void OpcoesLlm_DimensaoDiferenteDaColuna_ImpedeASubidaExplicandoAMigration(string dimensoes)
    {
        Should.Throw<InvalidOperationException>(() => ComVariaveis(("EMBEDDING_DIMENSIONS", dimensoes)).OpcoesLlm())
            .Message.ShouldContain("exige uma migration");
    }

    [Fact]
    public void FabricaGeradorEmbeddings_OpenAiCompativel_UsaOModeloDeEmbeddingConfigurado()
    {
        var opcoes = ComVariaveis(("LLM_PROVIDER", "openai-compatible"), ("LLM_BASE_URL", "https://exemplo.local/v1/"),
            ("LLM_API_KEY", ChaveFicticia), ("LLM_EMBEDDING_MODEL", "embedding-x")).OpcoesLlm();

        using var gerador = FabricaGeradorEmbeddings.Criar(opcoes);

        opcoes.ModeloEmbeddingEfetivo.ShouldBe("embedding-x");
        gerador.GetService<EmbeddingGeneratorMetadata>()!.DefaultModelId.ShouldBe("embedding-x");
    }

    [Fact]
    public void OpcoesRag_SemVariaveis_UsaOsPadroesDoAdr0011()
    {
        ComVariaveis().OpcoesRag().ShouldBe(new OpcoesRag(3, 0.35));
    }

    [Fact]
    public void OpcoesRag_ValoresValidos_SaoLidosComPontoDecimal()
    {
        ComVariaveis(("RAG_TOP_K", "5"), ("RAG_MIN_SIMILARITY", "0.5")).OpcoesRag().ShouldBe(new OpcoesRag(5, 0.5));
    }

    [Theory]
    [InlineData("RAG_TOP_K", "0")]
    [InlineData("RAG_TOP_K", "50")]
    [InlineData("RAG_MIN_SIMILARITY", "0,5")]
    [InlineData("RAG_MIN_SIMILARITY", "1.5")]
    public void OpcoesRag_ValorInvalido_ImpedeASubida(string chave, string valor)
    {
        Should.Throw<InvalidOperationException>(() => ComVariaveis((chave, valor)).OpcoesRag())
            .Message.ShouldStartWith($"A variável {chave}");
    }

    [Theory]
    [InlineData(null, "triagem.v2")]
    [InlineData("triagem.v1", "triagem.v1")]
    [InlineData("triagem.v2", "triagem.v2")]
    public void VersaoPromptTriagem_AusenteOuValida_UsaOPadraoOuAInformada(string? valor, string esperada)
    {
        ComVariaveis(("TRIAGEM_PROMPT_VERSAO", valor ?? "")).VersaoPromptTriagem().ShouldBe(esperada);
    }

    [Theory]
    [InlineData("../segredo")]
    [InlineData("copiloto.v1")]
    [InlineData("triagem.v2-teste")]
    public void VersaoPromptTriagem_ForaDoFormato_ImpedeASubida(string valor)
    {
        Should.Throw<InvalidOperationException>(() => ComVariaveis(("TRIAGEM_PROMPT_VERSAO", valor)).VersaoPromptTriagem())
            .Message.ShouldStartWith("A variável TRIAGEM_PROMPT_VERSAO");
    }
}
