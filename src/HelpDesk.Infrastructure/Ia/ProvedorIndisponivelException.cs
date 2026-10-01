namespace HelpDesk.Infrastructure.Ia;

/// <summary>
/// Falha transitória do provedor (429, 5xx, timeout): vale tentar de novo. <see cref="Tipo"/> é o que vai para
/// <c>uso_llm.erro_tipo</c> e para os logs; <see cref="RetryAfter"/> é o tempo pedido pelo provedor, se houver.
/// </summary>
public sealed class ProvedorIndisponivelException(string tipo, string mensagem, TimeSpan? retryAfter = null)
    : Exception(mensagem)
{
    public const string TipoRateLimit = "rate_limit";
    public const string TipoIndisponivel = "indisponivel";
    public const string TipoTimeout = "timeout";

    public string Tipo { get; } = tipo;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
