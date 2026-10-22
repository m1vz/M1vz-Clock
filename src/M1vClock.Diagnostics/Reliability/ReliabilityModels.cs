using M1vClock.Core.Results;

namespace M1vClock.Diagnostics.Reliability;

public enum ReliabilityKind
{
    UpdateInstalled,
    UpdateFailed,
    SoftwareInstalled,
    SoftwareRemoved,
    SoftwareReconfigured,
    UnexpectedShutdown,
    AppCrash,
    WindowsFailure,
    Other,
}

public enum StabilityVerdict
{
    Stable,
    Occasional,
    Unstable,
    Unknown,
}

public sealed record ReliabilityRecord(DateTimeOffset Time, string Source, int Id, string Product, ReliabilityKind Kind)
{
    public bool IsFailure => Kind is ReliabilityKind.UpdateFailed or ReliabilityKind.UnexpectedShutdown or ReliabilityKind.AppCrash or ReliabilityKind.WindowsFailure;
}

public sealed record StabilityPoint(DateTimeOffset At, double Index);

public sealed record ReliabilityReport(
    TimeSpan Window,
    ReadResult<IReadOnlyList<StabilityPoint>> Stability,
    ReadResult<IReadOnlyList<ReliabilityRecord>> Records)
{
    public const double StableFrom = 8;
    public const double OccasionalFrom = 5;

    public double? Current => Stability.TryGet(out var points) && points.Count > 0 ? points[^1].Index : null;

    public double? Lowest => Stability.TryGet(out var points) && points.Count > 0 ? points.Min(p => p.Index) : null;

    public double? Average => Stability.TryGet(out var points) && points.Count > 0 ? points.Average(p => p.Index) : null;

    public StabilityVerdict Verdict => VerdictOf(Current);

    public static StabilityVerdict VerdictOf(double? index) => index switch
    {
        null => StabilityVerdict.Unknown,
        >= StableFrom => StabilityVerdict.Stable,
        >= OccasionalFrom => StabilityVerdict.Occasional,
        _ => StabilityVerdict.Unstable,
    };

    public int Count(ReliabilityKind kind) => Records.TryGet(out var records) ? records.Count(r => r.Kind == kind) : 0;

    public int Failures => Records.TryGet(out var records) ? records.Count(r => r.IsFailure) : 0;
}

public interface IReliabilitySource
{
    ReadResult<IReadOnlyList<StabilityPoint>> ReadStability(TimeSpan window, CancellationToken cancellationToken);

    ReadResult<IReadOnlyList<ReliabilityRecord>> ReadRecords(TimeSpan window, CancellationToken cancellationToken);
}
