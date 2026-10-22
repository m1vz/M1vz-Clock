namespace M1vClock.Hardware.Network;

public static class NetworkEvaluator
{
    public const double RouterLossPercent = 2;
    public const double RouterLatencyMs = 30;
    public const double RouterJitterMs = 15;
    public const double InternetLossPercent = 1;
    public const double InternetBadLossPercent = 5;
    public const double InternetLatencyMs = 80;
    public const double InternetBadLatencyMs = 150;
    public const double InternetJitterMs = 20;
    public const double BufferbloatMs = 100;
    public const double SlowDnsMs = 150;
    public const double LowDownloadMbps = 10;

    public static NetworkAssessment Evaluate(NetworkMeasurements measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        var findings = new List<NetworkFinding>();
        var router = measurements.Router;
        var answering = measurements.Internet.Where(p => p.Answered).ToList();
        var localTrouble = router.Answered && (router.LossPercent > RouterLossPercent || router.AverageMs > RouterLatencyMs || router.JitterMs > RouterJitterMs);
        var bad = false;

        if (localTrouble)
        {
            findings.Add(new NetworkFinding(NetworkFindingKind.RouterUnstable, router.AverageMs, router.LossPercent, router.JitterMs));
        }

        if (!router.Answered)
        {
            findings.Add(new NetworkFinding(NetworkFindingKind.RouterSilent));
        }

        if (answering.Count == 0)
        {
            bad = true;
            findings.Add(new NetworkFinding(NetworkFindingKind.InternetUnreachable));
        }
        else
        {
            bad = CheckInternet(measurements, answering, localTrouble, findings);
        }

        CheckLoad(measurements, answering, findings);
        return new NetworkAssessment(bad ? NetworkVerdict.Bad : findings.Any(f => f.Kind != NetworkFindingKind.RouterSilent) ? NetworkVerdict.Warning : NetworkVerdict.Stable, findings);
    }

    private static bool CheckInternet(NetworkMeasurements measurements, List<PingStats> answering, bool localTrouble, List<NetworkFinding> findings)
    {
        var loss = measurements.Internet.Average(p => p.LossPercent);
        var latency = answering.Average(p => p.AverageMs);
        var jitter = answering.Max(p => p.JitterMs);
        if (loss > InternetLossPercent)
        {
            findings.Add(new NetworkFinding(localTrouble ? NetworkFindingKind.InternetLossLocal : NetworkFindingKind.InternetLossProvider, loss));
        }

        if (latency > InternetLatencyMs)
        {
            findings.Add(new NetworkFinding(localTrouble ? NetworkFindingKind.HighLatencyLocal : NetworkFindingKind.HighLatencyProvider, latency));
        }

        if (jitter > InternetJitterMs)
        {
            findings.Add(new NetworkFinding(NetworkFindingKind.HighJitter, jitter));
        }

        return loss > InternetBadLossPercent || latency > InternetBadLatencyMs;
    }

    private static void CheckLoad(NetworkMeasurements measurements, List<PingStats> answering, List<NetworkFinding> findings)
    {
        if (measurements.LoadedLatencyMs is { } loaded && answering.Count > 0 && loaded - answering.Min(p => p.AverageMs) > BufferbloatMs)
        {
            findings.Add(new NetworkFinding(NetworkFindingKind.Bufferbloat, loaded));
        }

        if (measurements.DnsMilliseconds is { } dns && dns > SlowDnsMs)
        {
            findings.Add(new NetworkFinding(NetworkFindingKind.SlowDns, dns));
        }

        if (measurements.DownloadMbps is { } download && download < LowDownloadMbps)
        {
            findings.Add(new NetworkFinding(NetworkFindingKind.LowDownload, download));
        }

        if (measurements.SpeedTestFailed && measurements.DownloadMbps is null)
        {
            findings.Add(new NetworkFinding(NetworkFindingKind.SpeedTestUnavailable));
        }
    }
}
