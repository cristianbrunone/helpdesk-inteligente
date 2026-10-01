using HelpDesk.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDesk.UnitTests.Worker;

public sealed class HeartbeatTests
{
    [Fact]
    public async Task ExecuteAsync_IntervaloCurto_RegistraBatimentosEEncerraAoCancelar()
    {
        var logger = new LoggerEmMemoria();
        using var heartbeat = new Heartbeat(logger, TimeSpan.FromMilliseconds(20));

        await heartbeat.StartAsync(TestContext.Current.CancellationToken);
        await EsperarAsync(() => logger.Mensagens.Count(m => m.Contains("heartbeat ", StringComparison.Ordinal)) >= 3);
        await heartbeat.StopAsync(TestContext.Current.CancellationToken);

        logger.Mensagens.First().ShouldContain("Worker iniciado");
        logger.Mensagens.ShouldContain(m => m.Contains("Worker ativo (heartbeat 1)", StringComparison.Ordinal));
        logger.Mensagens.Last().ShouldContain("Worker encerrado");
        heartbeat.ExecuteTask!.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private static async Task EsperarAsync(Func<bool> condicao)
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!condicao())
        {
            DateTime.UtcNow.ShouldBeLessThan(limite, "O heartbeat não registrou batimentos a tempo.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class LoggerEmMemoria : ILogger<Heartbeat>
    {
        private readonly List<string> _mensagens = [];

        public IReadOnlyList<string> Mensagens
        {
            get
            {
                lock (_mensagens)
                {
                    return [.. _mensagens];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_mensagens)
            {
                _mensagens.Add(formatter(state, exception));
            }
        }
    }
}
