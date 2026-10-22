using M1vClock.Core.RegistryValues;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Tests;

internal sealed class FakeWmiReader : IWmiReader
{
    private readonly Dictionary<string, List<WmiRow>> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _failures = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<(string SourceDeviceId, WmiRow Related)>> _related = new(StringComparer.OrdinalIgnoreCase);

    public List<WmiQuery> Queries { get; } = [];

    public List<(string Source, string Related)> RelatedQueries { get; } = [];

    public FakeWmiReader With(string className, params WmiRow[] rows)
    {
        _rows[className] = [.. rows];
        _failures.Remove(className);
        return this;
    }

    public FakeWmiReader WithRelated(string relatedClass, string sourceDeviceId, WmiRow related)
    {
        if (!_related.TryGetValue(relatedClass, out var list))
        {
            list = [];
            _related[relatedClass] = list;
        }

        list.Add((sourceDeviceId, related));
        _failures.Remove(relatedClass);
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

    public IReadOnlyList<WmiRelation> QueryRelated(WmiQuery source, string relatedClass)
    {
        ArgumentNullException.ThrowIfNull(source);
        RelatedQueries.Add((source.ClassName, relatedClass));
        if (_failures.TryGetValue(relatedClass, out var failure))
        {
            throw failure;
        }

        var sources = _rows.TryGetValue(source.ClassName, out var rows) ? rows : [];
        var related = _related.TryGetValue(relatedClass, out var list) ? list : [];
        return [.. sources.Select(s => new WmiRelation(s, [.. related.Where(r => r.SourceDeviceId == s.Text("DeviceId")).Select(r => r.Related)]))];
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
    private readonly Lock _gate = new();
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var entry = new LogEntry(logLevel, eventId, formatter(state, exception), exception);
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }
}
