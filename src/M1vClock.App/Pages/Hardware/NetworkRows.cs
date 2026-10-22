using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Hardware.Network;

namespace M1vClock.App.Pages.Hardware;

public static class NetworkRows
{
    public static string KindText(NetworkAdapterKind kind) => Loc.Get($"Hw.Net.Kind.{kind}");

    public static string LinkText(NetworkAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return adapter.LinkMbps is { } mbps ? Loc.Format("Hw.Net.Link", mbps.ToString("N0", Loc.Culture)) : Units.Missing;
    }

    public static IReadOnlyList<InfoRowViewModel> Adapter(NetworkAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return
        [
            Row("Hw.Net.Row.Adapter", adapter.Name),
            Row("Hw.Net.Row.Kind", KindText(adapter.Kind)),
            Row("Hw.Net.Row.Link", LinkText(adapter)),
            Row("Hw.Net.Row.Ip", adapter.Ipv4 ?? Units.Missing),
            Row("Hw.Net.Row.Gateway", adapter.Gateway ?? Units.Missing),
            Row("Hw.Net.Row.Dns", adapter.DnsServers.Count > 0 ? string.Join(", ", adapter.DnsServers) : Units.Missing),
            new InfoRowViewModel(Loc.Get("Hw.Net.Row.Mac"), adapter.MacAddress.Display(reveal: true), isSensitive: true),
        ];
    }

    public static string Summary(NetworkAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return string.Join(" · ", new[] { KindText(adapter.Kind), adapter.LinkMbps is null ? string.Empty : LinkText(adapter), adapter.Ipv4 ?? string.Empty }.Where(p => p.Length > 0));
    }

    public static IReadOnlyList<InfoRowViewModel> Others(IReadOnlyList<NetworkAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        return [.. adapters.Select(a => new InfoRowViewModel(a.Name, Summary(a)))];
    }

    public static string Verdict(NetworkVerdict verdict) => Loc.Get($"Hw.Diag.Verdict.{verdict}");

    public static MetricState Tone(NetworkVerdict verdict) => verdict switch
    {
        NetworkVerdict.Stable => MetricState.Good,
        NetworkVerdict.Warning => MetricState.Warning,
        _ => MetricState.Critical,
    };

    public static string Stage(NetworkStage stage) => Loc.Get($"Hw.Diag.Stage.{stage}");

    public static string Finding(NetworkFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var key = $"Hw.Diag.Finding.{finding.Kind}";
        return finding.Kind switch
        {
            NetworkFindingKind.RouterUnstable => Loc.Format(key, Whole(finding.First), Decimal(finding.Second), Whole(finding.Third)),
            NetworkFindingKind.InternetLossLocal or NetworkFindingKind.InternetLossProvider or NetworkFindingKind.LowDownload => Loc.Format(key, Decimal(finding.First)),
            NetworkFindingKind.HighLatencyLocal
                or NetworkFindingKind.HighLatencyProvider
                or NetworkFindingKind.HighJitter
                or NetworkFindingKind.Bufferbloat
                or NetworkFindingKind.SlowDns => Loc.Format(key, Whole(finding.First)),
            _ => Loc.Get(key),
        };
    }

    public static IReadOnlyList<string> Notes(NetworkAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        return assessment.Findings.Count == 0 ? [Loc.Get("Hw.Diag.Finding.AllGood")] : [.. assessment.Findings.Select(Finding)];
    }

    public static string PingText(PingStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return stats.Answered
            ? Loc.Format("Hw.Diag.Ping.Text", Decimal(stats.AverageMs), Decimal(stats.MinMs), Decimal(stats.MaxMs), Decimal(stats.JitterMs), Decimal(stats.LossPercent))
            : Loc.Get("Hw.Diag.Ping.Silent");
    }

    public static IReadOnlyList<InfoRowViewModel> Metrics(NetworkMeasurements measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        var rows = new List<InfoRowViewModel> { PingRow(measurements.Router) };
        rows.AddRange(measurements.Internet.Select(PingRow));
        if (measurements.DnsMilliseconds is { } dns)
        {
            rows.Add(Row("Hw.Diag.Metric.Dns", Loc.Format("Hw.Diag.Value.Ms", Whole(dns))));
        }

        rows.Add(Row("Hw.Diag.Metric.Download", Speed(measurements.DownloadMbps)));
        rows.Add(Row("Hw.Diag.Metric.Upload", Speed(measurements.UploadMbps)));
        if (measurements.LoadedLatencyMs is { } loaded)
        {
            rows.Add(Row("Hw.Diag.Metric.Loaded", Loc.Format("Hw.Diag.Value.Loaded", Whole(loaded))));
        }

        return rows;
    }

    private static InfoRowViewModel PingRow(PingStats stats) => new(Loc.Format(RoleKey(stats.Role), stats.Address), PingText(stats));

    private static string RoleKey(PingRole role) => role switch
    {
        PingRole.Router => "Hw.Diag.Metric.Router",
        PingRole.InternetPrimary => "Hw.Diag.Metric.Primary",
        _ => "Hw.Diag.Metric.Secondary",
    };

    private static string Speed(double? mbps) => mbps is { } value ? Loc.Format("Hw.Diag.Value.Mbps", Decimal(value)) : Units.Missing;

    private static string Whole(double value) => value.ToString("0", Loc.Culture);

    private static string Decimal(double value) => value.ToString("0.#", Loc.Culture);

    private static InfoRowViewModel Row(string labelKey, string value) => new(Loc.Get(labelKey), value);
}
