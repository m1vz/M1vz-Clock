using M1vClock.Core.Results;

namespace M1vClock.Diagnostics.Events;

public enum EventSeverity
{
    Critical = 1,
    Error = 2,
    Warning = 3,
}

public sealed record RawEvent(string Log, string Provider, int Id, EventSeverity Severity, DateTimeOffset Time, string? Message);

public sealed record RawEventBatch(IReadOnlyList<RawEvent> Events, bool Truncated);

public sealed record EventGroup(
    string Log,
    string Provider,
    int Id,
    EventSeverity Severity,
    int Count,
    DateTimeOffset First,
    DateTimeOffset Last,
    string? Message,
    KnownEvent? Known)
{
    public const int NoisyThreshold = 1000;

    public bool IsNoisy => Count >= NoisyThreshold;

    public string Key => $"{Log}/{Provider}/{Id}";
}

public sealed record EventReport(
    TimeSpan Window,
    IReadOnlyList<EventGroup> Groups,
    IReadOnlyDictionary<string, string> FailedLogs,
    bool Truncated)
{
    public int Total => Groups.Sum(g => g.Count);

    public int CountOf(EventSeverity severity) => Groups.Where(g => g.Severity == severity).Sum(g => g.Count);
}

public sealed record KnownEvent(string Key, KnownEventKind Kind);

public enum KnownEventKind
{
    UnexpectedShutdown,
    Hardware,
    Disk,
    Display,
    Service,
    Application,
    Update,
    Noise,
}

public interface IEventLogSource
{
    ReadResult<RawEventBatch> Read(string log, TimeSpan window, int limit, CancellationToken cancellationToken);
}
