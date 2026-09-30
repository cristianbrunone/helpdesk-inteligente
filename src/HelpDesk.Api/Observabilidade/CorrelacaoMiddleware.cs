using System.Diagnostics;
using HelpDesk.Api.Endpoints;

namespace HelpDesk.Api.Observabilidade;

/// <summary>
/// Correlation id de ponta a ponta (contrato §1, ADR-0016): aceita ou gera o <c>X-Correlation-Id</c>, devolve-o na
/// resposta, abre o scope <c>CorrelationId</c> nos logs e registra uma linha por requisição.
/// </summary>
internal sealed partial class CorrelacaoMiddleware(RequestDelegate next, ILogger<CorrelacaoMiddleware> logger)
{
    public const string Header = "X-Correlation-Id";
    private const int TamanhoMaximo = 64;
    private static readonly object _chave = new();

    public async Task InvokeAsync(HttpContext http)
    {
        var correlationId = Aceitar(http.Request.Headers[Header].ToString()) ?? Guid.CreateVersion7().ToString();
        http.Items[_chave] = correlationId;
        http.Response.OnStarting(() =>
        {
            http.Response.Headers[Header] = correlationId;
            return Task.CompletedTask;
        });

        using var escopo = logger.BeginScope(new EscopoCorrelacao(correlationId));
        var inicio = Stopwatch.GetTimestamp();
        try
        {
            await next(http);
        }
        finally
        {
            // Rota como template (/api/chamados/{id}): nunca o path cru, a query string, headers ou o corpo (NFR-06).
            var rota = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(sem rota)";
            var status = http.Response.StatusCode;

            // O healthcheck do Docker chama /health a cada poucos segundos: sucesso vai para Debug, falha continua visível.
            var nivel = rota == SaudeEndpoints.Rota && status < StatusCodes.Status400BadRequest
                ? LogLevel.Debug
                : LogLevel.Information;

            LogRequisicao(logger, nivel, http.Request.Method, rota, status,
                Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
        }
    }

    public static string Obter(HttpContext http) =>
        http.Items.TryGetValue(_chave, out var valor) && valor is string id ? id : http.TraceIdentifier;

    // Valor vindo do cliente vai para logs e headers: só aceito se for curto e sem caracteres de controle.
    private static string? Aceitar(string valor) =>
        valor.Length is > 0 and <= TamanhoMaximo && valor.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            ? valor
            : null;

    [LoggerMessage(Message = "HTTP {Metodo} {Rota} respondeu {Status} em {DuracaoMs:0.0} ms")]
    private static partial void LogRequisicao(ILogger logger, LogLevel nivel, string metodo, string rota, int status,
        double duracaoMs);

    /// <summary>Scope estruturado: o formatter JSON grava a propriedade <c>CorrelationId</c> e o texto legível.</summary>
    private sealed class EscopoCorrelacao(string correlationId) : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly KeyValuePair<string, object> _par = new("CorrelationId", correlationId);

        public int Count => 1;

        public KeyValuePair<string, object> this[int index] =>
            index == 0 ? _par : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            yield return _par;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"CorrelationId:{_par.Value}";
    }
}
