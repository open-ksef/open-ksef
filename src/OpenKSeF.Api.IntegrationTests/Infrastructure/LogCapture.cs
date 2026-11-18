using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OpenKSeF.Api.IntegrationTests.Infrastructure;

public sealed class LogCapture(LogLevel minimum) : ILoggerProvider, ILogger
{
    public ConcurrentQueue<string> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
            Entries.Enqueue($"{formatter(state, exception)} {exception}");
    }
    public void Dispose() { }
}
