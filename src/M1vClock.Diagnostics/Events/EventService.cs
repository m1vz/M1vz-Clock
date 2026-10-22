using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Diagnostics.Events;

public interface IEventService
{
    Task<EventReport> ReadAsync(TimeSpan window, CancellationToken cancellationToken);
}

public sealed class EventService(IEventLogSource source, ILogger<EventService> logger) : IEventService
{
    public const int EventsPerLog = 60_000;
    public const int MessageLength = 500;

    public static readonly IReadOnlyList<string> Logs = ["System", "Application"];

    public static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(90);

    public async Task<EventReport> ReadAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        if (window <= TimeSpan.Zero || window > MaximumWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "A janela de eventos vai de zero a 90 dias.");
        }

        var reads = Logs.Select(log => Task.Run(() => (Log: log, Result: source.Read(log, window, EventsPerLog, cancellationToken)), cancellationToken)).ToList();
        var done = await Task.WhenAll(reads).ConfigureAwait(false);
        var failed = new Dictionary<string, string>(StringComparer.Ordinal);
        var raw = new List<RawEvent>();
        var truncated = false;
        foreach (var (log, result) in done)
        {
            if (!result.TryGet(out var batch))
            {
                failed[log] = result.Error ?? "Leitura sem resposta.";
                continue;
            }

            raw.AddRange(batch.Events);
            truncated |= batch.Truncated;
            if (batch.Truncated)
            {
                DiagnosticsLog.EventLogTruncated(logger, log, EventsPerLog);
            }
        }

        return new EventReport(window, Group(raw), failed, truncated);
    }

    public static IReadOnlyList<EventGroup> Group(IEnumerable<RawEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events
            .GroupBy(e => (e.Log, Provider: e.Provider.ToUpperInvariant(), e.Id))
            .Select(BuildGroup)
            .OrderBy(g => g.IsNoisy)
            .ThenBy(g => g.Known is { Kind: KnownEventKind.Noise })
            .ThenBy(g => g.Severity)
            .ThenByDescending(g => g.Last)
            .ToList();
    }

    private static EventGroup BuildGroup(IGrouping<(string Log, string Provider, int Id), RawEvent> group)
    {
        var ordered = group.OrderBy(e => e.Time).ToList();
        var newest = ordered[^1];
        var message = ordered.AsEnumerable().Reverse().Select(e => e.Message).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
        return new EventGroup(
            newest.Log,
            newest.Provider,
            newest.Id,
            group.Min(e => e.Severity),
            ordered.Count,
            ordered[0].Time,
            newest.Time,
            Shorten(message),
            EventKnowledge.Find(newest.Provider, newest.Id));
    }

    private static string? Shorten(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var flat = string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= MessageLength ? flat : string.Concat(flat.AsSpan(0, MessageLength - 1), "…");
    }
}
