using M1vClock.Core.Privacy;

namespace M1vClock.Hardware.Network;

public enum NetworkAdapterKind
{
    Ethernet,
    Wifi,
    Other,
}

public sealed record NetworkAdapter(
    string Name,
    string Description,
    NetworkAdapterKind Kind,
    long LinkBitsPerSecond,
    string? Ipv4,
    string? Gateway,
    IReadOnlyList<string> DnsServers,
    SensitiveText MacAddress)
{
    public double? LinkMbps => LinkBitsPerSecond > 0 ? LinkBitsPerSecond / 1_000_000.0 : null;
}

public sealed record NetworkOverview(NetworkAdapter? Active, IReadOnlyList<NetworkAdapter> Others);

public enum PingRole
{
    Router,
    InternetPrimary,
    InternetSecondary,
}

public sealed record PingStats(PingRole Role, string Address, int Sent, int Received, double MinMs, double AverageMs, double MaxMs, double JitterMs)
{
    public int Lost => Sent - Received;

    public double LossPercent => Sent == 0 ? 0 : 100.0 * Lost / Sent;

    public bool Answered => Received > 0;
}

public sealed record NetworkMeasurements(
    PingStats Router,
    IReadOnlyList<PingStats> Internet,
    double? DnsMilliseconds,
    double? DownloadMbps,
    double? UploadMbps,
    double? LoadedLatencyMs,
    bool SpeedTestFailed);

public enum NetworkVerdict
{
    Stable,
    Warning,
    Bad,
}

public enum NetworkFindingKind
{
    RouterUnstable,
    RouterSilent,
    InternetUnreachable,
    InternetLossLocal,
    InternetLossProvider,
    HighLatencyLocal,
    HighLatencyProvider,
    HighJitter,
    Bufferbloat,
    SlowDns,
    LowDownload,
    SpeedTestUnavailable,
}

public sealed record NetworkFinding(NetworkFindingKind Kind, double First = 0, double Second = 0, double Third = 0);

public sealed record NetworkAssessment(NetworkVerdict Verdict, IReadOnlyList<NetworkFinding> Findings);

public sealed record NetworkTestReport(
    NetworkAdapter Adapter,
    NetworkMeasurements Measurements,
    NetworkAssessment Assessment,
    DateTimeOffset FinishedAt);

public enum NetworkStage
{
    FindingConnection,
    Latency,
    Dns,
    Download,
    Upload,
    Analyzing,
    Done,
}

public sealed record NetworkProgress(NetworkStage Stage, double Fraction);
