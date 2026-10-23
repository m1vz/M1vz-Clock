using System.Globalization;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.Diagnostics.Reliability;

namespace M1vClock.App.Pages.Diagnostics;

public static class ReliabilityRows
{
    public static DiagnosticRowViewModel Record(ReliabilityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new DiagnosticRowViewModel(
            Loc.Get($"Diag.Reliability.Kind.{record.Kind}"),
            record.Product,
            Loc.Format("Diag.Events.Source", record.Source, record.Id.ToString(Loc.Culture)),
            record.IsFailure ? Loc.Get("Diag.Severity.Error") : string.Empty,
            record.IsFailure ? MetricState.Warning : MetricState.Neutral,
            CrashRows.Time(record.Time),
            string.Empty);
    }

    public static string Index(double? value) => value is { } index ? index.ToString("0.0#", Loc.Culture) : "—";

    public static string Verdict(StabilityVerdict verdict) => Loc.Get($"Diag.Reliability.Verdict.{verdict}");

    public static MetricState Tone(StabilityVerdict verdict) => verdict switch
    {
        StabilityVerdict.Stable => MetricState.Good,
        StabilityVerdict.Occasional => MetricState.Warning,
        StabilityVerdict.Unstable => MetricState.Critical,
        _ => MetricState.Neutral,
    };

    public static IReadOnlyList<ChartPoint> ChartPoints(IReadOnlyList<StabilityPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return points.Select(p => new ChartPoint(p.At.ToUnixTimeSeconds() / 86400.0, p.Index)).ToList();
    }

    public static IReadOnlyList<double> FailureMarkers(IEnumerable<ReliabilityRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records.Where(r => r.IsFailure).Select(r => r.Time.ToUnixTimeSeconds() / 86400.0).Order().ToList();
    }

    public static string ChartName(double? current) => Loc.Format("Diag.Reliability.Chart.Name", Index(current).ToString(CultureInfo.InvariantCulture));
}
