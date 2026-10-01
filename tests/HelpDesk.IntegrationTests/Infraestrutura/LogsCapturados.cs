using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace HelpDesk.IntegrationTests.Infraestrutura;

/// <summary>Provider de log em memória que guarda mensagem, nível e os valores dos scopes ativos.</summary>
public sealed class LogsCapturados : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<Registro> _registros = new();
    private IExternalScopeProvider _escopos = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<Registro> Registros => _registros;

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _escopos = scopeProvider;

    public void Dispose()
    {
    }

    public sealed record Registro(string Categoria, LogLevel Nivel, string Mensagem, IReadOnlyDictionary<string, object?> Escopo);

    private sealed class Logger(string categoria, LogsCapturados provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._escopos.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var escopo = new Dictionary<string, object?>();
            provider._escopos.ForEachScope((valor, destino) =>
            {
                if (valor is IEnumerable<KeyValuePair<string, object>> pares)
                {
                    foreach (var (chave, item) in pares)
                    {
                        destino[chave] = item;
                    }
                }
            }, escopo);

            provider._registros.Enqueue(new Registro(categoria, logLevel, formatter(state, exception), escopo));
        }
    }
}
