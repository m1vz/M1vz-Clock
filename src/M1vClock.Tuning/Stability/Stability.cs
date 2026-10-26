using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace M1vClock.Tuning.Stability;

public enum HardwareErrorKind
{
    Whea,
    DriverReset,
    UnexpectedShutdown,
}

public sealed record HardwareErrorEvent(DateTimeOffset Time, HardwareErrorKind Kind, string Provider, int EventId);

public sealed record HardwareErrorReport(IReadOnlyList<HardwareErrorEvent> Events, string? ReadError = null)
{
    public static HardwareErrorReport Empty { get; } = new([]);

    public int Whea => Events.Count(e => e.Kind == HardwareErrorKind.Whea);

    public int DriverResets => Events.Count(e => e.Kind == HardwareErrorKind.DriverReset);

    public int UnexpectedShutdowns => Events.Count(e => e.Kind == HardwareErrorKind.UnexpectedShutdown);

    public int Total => Events.Count;

    public HardwareErrorEvent? Last => Events.MaxBy(e => e.Time);

    public bool CouldRead => ReadError is null;
}

public interface IHardwareErrorSource
{
    HardwareErrorReport ErrorsSince(DateTimeOffset since);
}

public static class HardwareErrorQuery
{
    public const string WheaProvider = "Microsoft-Windows-WHEA-Logger";
    public const string KernelPowerProvider = "Microsoft-Windows-Kernel-Power";

    public static string XPath(DateTimeOffset since)
    {
        var time = since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return "*[System[("
            + $"Provider[@Name='{WheaProvider}']"
            + " or (Provider[@Name='Display'] and EventID=4101)"
            + " or (Provider[@Name='nvlddmkm'] and (EventID=13 or EventID=153))"
            + $" or (Provider[@Name='{KernelPowerProvider}'] and EventID=41)"
            + $") and TimeCreated[@SystemTime>='{time}']]]";
    }

    public static HardwareErrorKind? Classify(string provider, int eventId) => provider switch
    {
        WheaProvider => HardwareErrorKind.Whea,
        "Display" when eventId == 4101 => HardwareErrorKind.DriverReset,
        "nvlddmkm" when eventId is 13 or 153 => HardwareErrorKind.DriverReset,
        KernelPowerProvider when eventId == 41 => HardwareErrorKind.UnexpectedShutdown,
        _ => null,
    };
}

public sealed class WindowsHardwareErrorSource(ILogger<WindowsHardwareErrorSource> logger) : IHardwareErrorSource
{
    public HardwareErrorReport ErrorsSince(DateTimeOffset since)
    {
        var events = new List<HardwareErrorEvent>();
        try
        {
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, HardwareErrorQuery.XPath(since)));
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    if (HardwareErrorQuery.Classify(record.ProviderName, record.Id) is { } kind && record.TimeCreated is { } created)
                    {
                        events.Add(new HardwareErrorEvent(new DateTimeOffset(created.ToUniversalTime(), TimeSpan.Zero), kind, record.ProviderName, record.Id));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            TuningLog.ErrorsUnreadable(logger, ex);
            return new HardwareErrorReport(events, ex.Message);
        }

        return new HardwareErrorReport(events);
    }
}

public enum StabilityTarget
{
    CpuAndMemory,
    Gpu,
    Everything,
}

public enum StabilityStop
{
    Completed,
    StoppedByUser,
    Temperature,
    MemoryErrors,
    WorkloadFailed,
    SensorLost,
    WindowUnresponsive,
    HardwareErrors,
}

public sealed record StabilityEvidence(
    StabilityTarget Target,
    StabilityStop Stop,
    TimeSpan Elapsed,
    TimeSpan Planned,
    long MemoryErrors,
    float? CpuMaximum,
    float? GpuMaximum,
    HardwareErrorReport Errors);

public enum StabilityResult
{
    Stable,
    Unstable,
    Inconclusive,
}

public enum StabilityReason
{
    WheaErrors,
    DriverResets,
    UnexpectedShutdown,
    MemoryErrors,
    WorkloadCrashed,
    TooHot,
    StoppedEarly,
    SensorLost,
    WindowUnresponsive,
    ErrorsUnreadable,
}

public sealed record StabilityVerdict(StabilityResult Result, IReadOnlyList<StabilityReason> Reasons);

public static class StabilityJudge
{
    public static readonly TimeSpan MinimumUseful = TimeSpan.FromMinutes(5);

    public static StabilityVerdict Decide(StabilityEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var faults = new List<StabilityReason>();
        if (evidence.Errors.Whea > 0)
        {
            faults.Add(StabilityReason.WheaErrors);
        }

        if (evidence.Errors.DriverResets > 0)
        {
            faults.Add(StabilityReason.DriverResets);
        }

        if (evidence.Errors.UnexpectedShutdowns > 0)
        {
            faults.Add(StabilityReason.UnexpectedShutdown);
        }

        if (evidence.MemoryErrors > 0 || evidence.Stop == StabilityStop.MemoryErrors)
        {
            faults.Add(StabilityReason.MemoryErrors);
        }

        if (evidence.Stop == StabilityStop.WorkloadFailed)
        {
            faults.Add(StabilityReason.WorkloadCrashed);
        }

        if (faults.Count > 0)
        {
            return new StabilityVerdict(StabilityResult.Unstable, faults);
        }

        var doubts = new List<StabilityReason>();
        switch (evidence.Stop)
        {
            case StabilityStop.Temperature:
                doubts.Add(StabilityReason.TooHot);
                break;
            case StabilityStop.SensorLost:
                doubts.Add(StabilityReason.SensorLost);
                break;
            case StabilityStop.WindowUnresponsive:
                doubts.Add(StabilityReason.WindowUnresponsive);
                break;
            case StabilityStop.StoppedByUser:
                doubts.Add(StabilityReason.StoppedEarly);
                break;
            default:
                break;
        }

        if (evidence.Stop == StabilityStop.Completed && evidence.Elapsed < MinimumUseful)
        {
            doubts.Add(StabilityReason.StoppedEarly);
        }

        if (!evidence.Errors.CouldRead)
        {
            doubts.Add(StabilityReason.ErrorsUnreadable);
        }

        return doubts.Count > 0
            ? new StabilityVerdict(StabilityResult.Inconclusive, doubts.Distinct().ToList())
            : new StabilityVerdict(StabilityResult.Stable, []);
    }
}
