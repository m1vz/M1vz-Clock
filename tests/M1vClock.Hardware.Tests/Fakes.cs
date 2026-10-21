using M1vClock.Core.RegistryValues;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Tests;

internal sealed class FakeWmiReader : IWmiReader
{
    private readonly Dictionary<string, List<WmiRow>> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _failures = new(StringComparer.OrdinalIgnoreCase);

    public List<WmiQuery> Queries { get; } = [];

    public FakeWmiReader With(string className, params WmiRow[] rows)
    {
        _rows[className] = [.. rows];
        return this;
    }

    public FakeWmiReader Failing(string className, Exception exception)
    {
        _failures[className] = exception;
        return this;
    }

    public IReadOnlyList<WmiRow> Query(WmiQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Queries.Add(query);
        if (_failures.TryGetValue(query.ClassName, out var failure))
        {
            throw failure;
        }

        return _rows.TryGetValue(query.ClassName, out var rows) ? rows : [];
    }
}

internal sealed class StubRegistry : IRegistryAccess
{
    private readonly Dictionary<string, RegistryValueState> _values = new(StringComparer.Ordinal);

    public StubRegistry With(string valueName, RegistryValueState state)
    {
        _values[valueName] = state;
        return this;
    }

    public RegistryValueState Read(RegistryValueAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return _values.TryGetValue(address.ValueName, out var state) ? state : RegistryValueState.Absent(keyExists: true);
    }

    public void Write(RegistryValueAddress address, RegistryValueState state) =>
        throw new NotSupportedException("A leitura de hardware nunca escreve no registro.");
}

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
