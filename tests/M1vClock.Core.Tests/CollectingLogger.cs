using Microsoft.Extensions.Logging;

namespace M1vClock.Core.Tests;

internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);

internal sealed class CollectingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }
}
