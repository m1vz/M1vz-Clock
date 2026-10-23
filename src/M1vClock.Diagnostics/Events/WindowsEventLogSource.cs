using System.Diagnostics.Eventing.Reader;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Diagnostics.Events;

public sealed class WindowsEventLogSource(ILogger<WindowsEventLogSource> logger) : IEventLogSource
{
    public ReadResult<RawEventBatch> Read(string log, TimeSpan window, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(log);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var milliseconds = (long)window.TotalMilliseconds;
        var query = new EventLogQuery(log, PathType.LogName, $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {milliseconds}]]]")
        {
            ReverseDirection = true,
        };
        try
        {
            using var reader = new EventLogReader(query);
            var events = new List<RawEvent>();
            var described = new HashSet<(string, int)>();
            var truncated = false;
            while (reader.ReadEvent() is { } record)
            {
                using (record)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (events.Count >= limit)
                    {
                        truncated = true;
                        break;
                    }

                    if (record.Level is not (>= 1 and <= 3) || record.TimeCreated is not { } time)
                    {
                        continue;
                    }

                    var provider = record.ProviderName ?? "?";
                    var message = described.Add((provider, record.Id)) ? Describe(record) : null;
                    events.Add(new RawEvent(log, provider, record.Id, (EventSeverity)record.Level.Value, new DateTimeOffset(time), message));
                }
            }

            return ReadResult.Success(new RawEventBatch(events, truncated));
        }
        catch (UnauthorizedAccessException)
        {
            DiagnosticsLog.EventLogDenied(logger, log);
            return ReadResult.Failure<RawEventBatch>($"Sem permissão para ler o log {log}.");
        }
        catch (EventLogException ex)
        {
            DiagnosticsLog.EventLogFailed(logger, ex, log);
            return ReadResult.Failure<RawEventBatch>(ex.Message);
        }
    }

    private string? Describe(EventRecord record)
    {
        try
        {
            return record.FormatDescription();
        }
        catch (EventLogException ex)
        {
            DiagnosticsLog.EventLogFailed(logger, ex, record.LogName ?? "?");
            return null;
        }
    }
}
