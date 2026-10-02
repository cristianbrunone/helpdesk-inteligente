namespace HelpDesk.Infrastructure.Ia;

public enum TipoProvedorLlm
{
    Fake,
    OpenAiCompativel,
}

/// <summary>Modos de falha do fake, para testar o pipeline e a resiliência sem provedor real (ADR-0005).</summary>
public enum ModoFake
{
    Normal,
    Lento,
    JsonInvalido,
    CategoriaInexistente,
    RateLimit,

    /// <summary>Copiloto: a resposta "vaza" um CPF e cita um chamado inexistente (teste do guardrail, ADR-0020).</summary>
    VazaDados,
}

/// <summary>
/// Configuração do provedor de LLM (ADR-0005), lida do ambiente na subida. A chave de API fica fora do
/// <see cref="ToString"/>: um log descuidado das opções não a expõe.
/// </summary>
public sealed class OpcoesLlm
{
    public const string NomeProvedorFake = "fake";
    public const string NomeProvedorOpenAiCompativel = "openai-compatible";
    public const string ModeloFake = "fake-triagem-v1";
    public const string ModeloEmbeddingFake = "fake-embedding-v1";

    public required TipoProvedorLlm Provedor { get; init; }

    public Uri? BaseUrl { get; init; }

    public string? ChaveApi { get; init; }

    public required string ModeloChat { get; init; }

    /// <summary>Modelo de embedding do provedor real (ADR-0011); a dimensão é sempre 768.</summary>
    public string ModeloEmbedding { get; init; } = Configuracao.LeitorAmbiente.OpcoesLlmPadrao.ModeloEmbedding;

    /// <summary>Tempo máximo de cada tentativa (NFR-04; 60 s após a PoC).</summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>Novas tentativas após a primeira, para falhas transitórias (429, 5xx, timeout).</summary>
    public required int MaxRetries { get; init; }

    /// <summary>Orçamento de saída da triagem (ADR-0021).</summary>
    public required int MaxTokensSaidaTriagem { get; init; }

    public ModoFake ModoFake { get; init; } = ModoFake.Normal;

    public TimeSpan AtrasoFake { get; init; } = TimeSpan.FromSeconds(30);

    public string NomeProvedor => Provedor == TipoProvedorLlm.Fake ? NomeProvedorFake : NomeProvedorOpenAiCompativel;

    /// <summary>O modelo efetivo: o fake tem nome próprio, para nunca se confundir com um modelo real nos registros.</summary>
    public string ModeloEfetivo => Provedor == TipoProvedorLlm.Fake ? ModeloFake : ModeloChat;

    /// <summary>
    /// O modelo de embedding efetivo, gravado em <c>documentos_rag.embedding_modelo</c>. Trocar de provedor ou de
    /// <c>LLM_EMBEDDING_MODEL</c> muda este nome, e o reconciliador reindexa tudo (ADR-0010).
    /// </summary>
    public string ModeloEmbeddingEfetivo => Provedor == TipoProvedorLlm.Fake ? ModeloEmbeddingFake : ModeloEmbedding;

    public override string ToString() =>
        $"{NomeProvedor} ({ModeloEfetivo} + {ModeloEmbeddingEfetivo}, timeout {Timeout.TotalSeconds:0} s, {MaxRetries} retries" +
        (Provedor == TipoProvedorLlm.Fake ? $", modo {ModoFake})" : $", {BaseUrl?.Host})");
}
