using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HelpDesk.Application.Conhecimento;
using HelpDesk.Application.Triagem;
using HelpDesk.Infrastructure.Ia;
using HelpDesk.Infrastructure.Seguranca;

namespace HelpDesk.Infrastructure.Configuracao;

/// <summary>
/// Leitura das variáveis de ambiente com duas regras (ADR-0023): valor vazio é "não configurado" (vale o padrão,
/// como numa linha <c>CHAVE=</c> do <c>.env</c>), e valor inválido derruba a subida com uma mensagem clara, em vez
/// de seguir com um comportamento inesperado. Recebe a função de leitura para não depender do host.
/// </summary>
public sealed class LeitorAmbiente(Func<string, string?> ler)
{
    public const string IaTriagemHabilitada = "IA_TRIAGEM_HABILITADA";
    public const string IaCopilotoHabilitado = "IA_COPILOTO_HABILITADO";
    public const string CopilotoRateLimitPorMinuto = "COPILOTO_RATE_LIMIT_POR_MINUTO";
    public const string JwtChave = "JWT_CHAVE";
    public const string SessaoCookieSeguro = "SESSAO_COOKIE_SEGURO";

    /// <summary>
    /// Sessão (ADR-0026). <c>JWT_CHAVE</c> é segredo: a mensagem de erro cita só o tamanho, nunca o valor. Sem ela, uma
    /// chave aleatória por subida. <c>SESSAO_COOKIE_SEGURO=false</c> só para acessar por HTTP a partir de outra máquina
    /// (pelo <c>localhost</c>, o navegador aceita o cookie <c>Secure</c> mesmo sem HTTPS).
    /// </summary>
    public OpcoesSessao OpcoesSessao()
    {
        var chave = Texto(JwtChave) is { } texto ? Encoding.UTF8.GetBytes(texto) : null;
        if (chave is not null && chave.Length < Seguranca.OpcoesSessao.ChaveTamanhoMinimo)
        {
            throw new InvalidOperationException(
                $"A variável {JwtChave} deve ter pelo menos {Seguranca.OpcoesSessao.ChaveTamanhoMinimo} bytes " +
                $"(HMAC-SHA256); tem {chave.Length}.");
        }

        return new OpcoesSessao(
            chave ?? RandomNumberGenerator.GetBytes(Seguranca.OpcoesSessao.ChaveTamanhoMinimo),
            ChaveGerada: chave is null,
            CookieSeguro: Booleano(SessaoCookieSeguro, padrao: true),
            Seguranca.OpcoesSessao.ValidadePadrao);
    }

    public string? Texto(string chave) => ler(chave) is { } valor && !string.IsNullOrWhiteSpace(valor)
        ? valor.Trim()
        : null;

    public bool Booleano(string chave, bool padrao) => Texto(chave) switch
    {
        null => padrao,
        var valor when bool.TryParse(valor, out var resultado) => resultado,
        var valor => throw Invalida(chave, valor, "'true' ou 'false'"),
    };

    public int Inteiro(string chave, int padrao, int minimo, int maximo) => Texto(chave) switch
    {
        null => padrao,
        var valor when int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero)
            && numero >= minimo && numero <= maximo => numero,
        var valor => throw Invalida(chave, valor, $"um inteiro entre {minimo} e {maximo}"),
    };

    public double Decimal(string chave, double padrao, double minimo, double maximo) => Texto(chave) switch
    {
        null => padrao,
        var valor when double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var numero)
            && numero >= minimo && numero <= maximo => numero,
        var valor => throw Invalida(chave, valor,
            $"um número entre {minimo.ToString(CultureInfo.InvariantCulture)} e " +
            $"{maximo.ToString(CultureInfo.InvariantCulture)}, com ponto decimal"),
    };

    public const string TriagemPromptVersao = "TRIAGEM_PROMPT_VERSAO";

    /// <summary>
    /// Versão do prompt da triagem (<c>prompts/{versao}.md</c>). Uma versão nova só vira a padrão depois do harness de
    /// evals (ADR-0018); esta variável permite comparar e voltar atrás sem rebuild.
    /// </summary>
    public string VersaoPromptTriagem() => Texto(TriagemPromptVersao) switch
    {
        null => MontadorPromptTriagem.VersaoPadrao,
        var valor when System.Text.RegularExpressions.Regex.IsMatch(valor, @"^triagem\.v[0-9]+$") => valor,
        var valor => throw Invalida(TriagemPromptVersao, valor, "uma versão no formato triagem.vN (ex.: triagem.v2)"),
    };

    public const string RagTopK = "RAG_TOP_K";
    public const string RagMinSimilarity = "RAG_MIN_SIMILARITY";

    /// <summary>Recuperação do RAG (ADR-0011): top-k por tipo de origem e similaridade mínima de cosseno.</summary>
    public OpcoesRag OpcoesRag() => new(
        Inteiro(RagTopK, Application.Conhecimento.OpcoesRag.TopKPadrao, 1, 20),
        Decimal(RagMinSimilarity, Application.Conhecimento.OpcoesRag.SimilaridadeMinimaPadrao, 0, 1));

    public const string LlmProvider = "LLM_PROVIDER";
    public const string LlmBaseUrl = "LLM_BASE_URL";
    public const string LlmApiKey = "LLM_API_KEY";
    public const string LlmChatModel = "LLM_CHAT_MODEL";
    public const string LlmEmbeddingModel = "LLM_EMBEDDING_MODEL";
    public const string EmbeddingDimensions = "EMBEDDING_DIMENSIONS";
    public const string LlmTimeoutSeconds = "LLM_TIMEOUT_SECONDS";
    public const string LlmMaxRetries = "LLM_MAX_RETRIES";
    public const string TriagemMaxTokensSaida = "TRIAGEM_MAX_TOKENS_SAIDA";
    public const string CopilotoMaxTokensSaida = "COPILOTO_MAX_TOKENS_SAIDA";
    public const string LlmFakeModo = "LLM_FAKE_MODO";
    public const string LlmFakeAtrasoMs = "LLM_FAKE_ATRASO_MS";

    public OpcoesIA OpcoesIA() => new(
        TriagemHabilitada: Booleano(IaTriagemHabilitada, padrao: true),
        CopilotoHabilitado: Booleano(IaCopilotoHabilitado, padrao: true));

    public int RateLimitCopilotoPorMinuto() => Inteiro(CopilotoRateLimitPorMinuto, padrao: 10, minimo: 1, maximo: 600);

    /// <summary>
    /// Provedor de LLM (ADR-0005). O padrão é o fake. Com <c>openai-compatible</c>, URL e chave são obrigatórias, e
    /// a mensagem de erro cita só o nome da variável, nunca o valor da chave.
    /// </summary>
    public OpcoesLlm OpcoesLlm()
    {
        var provedor = (Texto(LlmProvider) ?? OpcoesLlmPadrao.Provedor).ToLowerInvariant() switch
        {
            Ia.OpcoesLlm.NomeProvedorFake => TipoProvedorLlm.Fake,
            Ia.OpcoesLlm.NomeProvedorOpenAiCompativel => TipoProvedorLlm.OpenAiCompativel,
            var outro => throw Invalida(LlmProvider, outro, $"'{Ia.OpcoesLlm.NomeProvedorFake}' ou " +
                $"'{Ia.OpcoesLlm.NomeProvedorOpenAiCompativel}'"),
        };

        Uri? baseUrl = null;
        string? chave = null;
        if (provedor == TipoProvedorLlm.OpenAiCompativel)
        {
            var url = Texto(LlmBaseUrl)
                ?? throw new InvalidOperationException($"{LlmProvider}=openai-compatible exige {LlmBaseUrl}.");
            if (!Uri.TryCreate(url, UriKind.Absolute, out baseUrl) || baseUrl.Scheme is not ("https" or "http"))
            {
                // Sem ecoar o valor: há provedores que aceitam a chave na query string da URL.
                throw new InvalidOperationException($"A variável {LlmBaseUrl} deve ser uma URL http(s) absoluta.");
            }

            chave = Texto(LlmApiKey)
                ?? throw new InvalidOperationException($"{LlmProvider}=openai-compatible exige {LlmApiKey} no .env.");
        }

        // A dimensão é fixa na coluna vector(768) (ADR-0011): outro valor exige uma migration, não só a variável.
        if (Texto(EmbeddingDimensions) is { } dimensoes && dimensoes != $"{IGeradorEmbeddings.Dimensoes}")
        {
            throw new InvalidOperationException(
                $"A variável {EmbeddingDimensions} tem o valor '{dimensoes}', mas a coluna de embeddings tem " +
                $"{IGeradorEmbeddings.Dimensoes} dimensões. Outra dimensão exige uma migration (veja o README).");
        }

        return new OpcoesLlm
        {
            Provedor = provedor,
            BaseUrl = baseUrl,
            ChaveApi = chave,
            ModeloChat = Texto(LlmChatModel) ?? OpcoesLlmPadrao.ModeloChat,
            ModeloEmbedding = Texto(LlmEmbeddingModel) ?? OpcoesLlmPadrao.ModeloEmbedding,
            Timeout = TimeSpan.FromSeconds(Inteiro(LlmTimeoutSeconds, OpcoesLlmPadrao.TimeoutSegundos, 1, 600)),
            MaxRetries = Inteiro(LlmMaxRetries, OpcoesLlmPadrao.MaxRetries, 0, 10),
            MaxTokensSaidaTriagem = Inteiro(TriagemMaxTokensSaida, OpcoesLlmPadrao.MaxTokensSaidaTriagem, 50, 8192),
            MaxTokensSaidaCopiloto = Inteiro(CopilotoMaxTokensSaida, OpcoesLlmPadrao.MaxTokensSaidaCopiloto, 50, 8192),
            ModoFake = ModoDoFake(),
            AtrasoFake = TimeSpan.FromMilliseconds(Inteiro(LlmFakeAtrasoMs, OpcoesLlmPadrao.AtrasoFakeMs, 0, 600_000)),
        };
    }

    /// <summary>Destino OTLP dos traces (ADR-0019); <c>null</c> = sem exportação.</summary>
    public Uri? EndpointOtlp()
    {
        if (Texto(Observabilidade.Tracing.VariavelEndpoint) is not { } valor)
        {
            return null;
        }

        return Uri.TryCreate(valor, UriKind.Absolute, out var endpoint) && endpoint.Scheme is "http" or "https"
            ? endpoint
            : throw Invalida(Observabilidade.Tracing.VariavelEndpoint, valor, "uma URL http(s) absoluta");
    }

    private ModoFake ModoDoFake() => Texto(LlmFakeModo)?.ToLowerInvariant() switch
    {
        null or "normal" => ModoFake.Normal,
        "lento" => ModoFake.Lento,
        "json_invalido" => ModoFake.JsonInvalido,
        "categoria_inexistente" => ModoFake.CategoriaInexistente,
        "rate_limit" => ModoFake.RateLimit,
        "vaza_dados" => ModoFake.VazaDados,
        var outro => throw Invalida(LlmFakeModo, outro,
            "normal, lento, json_invalido, categoria_inexistente, rate_limit ou vaza_dados"),
    };

    /// <summary>Padrões do ADR-0005 (revisados na PoC) e do ADR-0021.</summary>
    public static class OpcoesLlmPadrao
    {
        public const string Provedor = Ia.OpcoesLlm.NomeProvedorFake;
        public const string ModeloChat = "gemini-3.5-flash-lite";
        public const string ModeloEmbedding = "gemini-embedding-001";
        public const int TimeoutSegundos = 60;
        public const int MaxRetries = 3;
        public const int MaxTokensSaidaTriagem = 800;
        public const int MaxTokensSaidaCopiloto = 800;
        public const int AtrasoFakeMs = 30_000;
    }

    // O valor só aparece na mensagem para variáveis que não são segredo (quem chama nunca passa a chave de API).
    private static InvalidOperationException Invalida(string chave, string valor, string esperado) =>
        new($"A variável {chave} tem o valor '{valor}', mas deve ser {esperado}.");
}
