using M1vClock.Hardware.Network;

namespace M1vClock.Hardware.Tests;

public sealed class PingStatisticsTests
{
    [Fact]
    public void SummarizesMinimumAverageMaximumAndJitter()
    {
        var stats = PingStatistics.Summarize(PingRole.InternetPrimary, "1.1.1.1", [10, 20, 15, 25], sent: 5);

        Assert.Equal(10, stats.MinMs);
        Assert.Equal(17.5, stats.AverageMs);
        Assert.Equal(25, stats.MaxMs);
        Assert.Equal((10 + 5 + 10) / 3.0, stats.JitterMs, 6);
        Assert.Equal(5, stats.Sent);
        Assert.Equal(4, stats.Received);
        Assert.Equal(1, stats.Lost);
        Assert.Equal(20, stats.LossPercent);
        Assert.True(stats.Answered);
    }

    [Fact]
    public void AStableLinkHasNoJitter()
    {
        Assert.Equal(0, PingStatistics.Jitter([12, 12, 12, 12]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void JitterNeedsAtLeastTwoSamples(int count)
    {
        Assert.Equal(0, PingStatistics.Jitter(Enumerable.Repeat(30.0, count).ToList()));
    }

    [Fact]
    public void NoAnswerMeansEverythingWasLost()
    {
        var stats = PingStatistics.Summarize(PingRole.Router, "192.168.0.1", [], sent: 30);

        Assert.False(stats.Answered);
        Assert.Equal(30, stats.Lost);
        Assert.Equal(100, stats.LossPercent);
        Assert.Equal(0, stats.AverageMs);
    }

    [Fact]
    public void NothingSentIsNotLoss()
    {
        Assert.Equal(0, PingStatistics.Summarize(PingRole.Router, "192.168.0.1", [], sent: 0).LossPercent);
    }

    [Fact]
    public void MoreAnswersThanPacketsIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PingStatistics.Summarize(PingRole.Router, "192.168.0.1", [1, 2, 3], sent: 2));
    }
}

public sealed class NetworkEvaluatorTests
{
    private static PingStats Ping(PingRole role, double average, double loss = 0, double jitter = 1, bool answered = true)
    {
        const int sent = 100;
        var received = answered ? (int)(sent - loss) : 0;
        return new PingStats(role, "x", sent, received, average, average, average, jitter);
    }

    private static NetworkMeasurements Healthy() => new(
        Ping(PingRole.Router, 2),
        [Ping(PingRole.InternetPrimary, 12), Ping(PingRole.InternetSecondary, 14)],
        DnsMilliseconds: 20,
        DownloadMbps: 300,
        UploadMbps: 100,
        LoadedLatencyMs: 25,
        SpeedTestFailed: false);

    private static IEnumerable<NetworkFindingKind> Kinds(NetworkAssessment assessment) => assessment.Findings.Select(f => f.Kind);

    [Fact]
    public void AHealthyConnectionIsStableWithNoFindings()
    {
        var assessment = NetworkEvaluator.Evaluate(Healthy());

        Assert.Equal(NetworkVerdict.Stable, assessment.Verdict);
        Assert.Empty(assessment.Findings);
    }

    [Fact]
    public void ARouterThatDoesNotAnswerIsOnlyANoteAndKeepsTheConnectionStable()
    {
        var assessment = NetworkEvaluator.Evaluate(Healthy() with { Router = Ping(PingRole.Router, 0, answered: false) });

        Assert.Equal(NetworkVerdict.Stable, assessment.Verdict);
        Assert.Equal([NetworkFindingKind.RouterSilent], Kinds(assessment));
    }

    [Theory]
    [InlineData(40, 0, 1)]
    [InlineData(2, 3, 1)]
    [InlineData(2, 0, 20)]
    public void AnUnstableRouterWarnsAboutTheLocalNetworkWithItsNumbers(double latency, double loss, double jitter)
    {
        var assessment = NetworkEvaluator.Evaluate(Healthy() with { Router = Ping(PingRole.Router, latency, loss, jitter) });

        Assert.Equal(NetworkVerdict.Warning, assessment.Verdict);
        var finding = Assert.Single(assessment.Findings);
        Assert.Equal(new NetworkFinding(NetworkFindingKind.RouterUnstable, latency, loss, jitter), finding);
    }

    [Fact]
    public void LossBlamesTheLocalNetworkOrTheProviderDependingOnTheRouter()
    {
        var lossy = Healthy().Internet.Select(p => p with { Received = 97 }).ToList();

        var provider = NetworkEvaluator.Evaluate(Healthy() with { Internet = lossy });
        var local = NetworkEvaluator.Evaluate(Healthy() with { Internet = lossy, Router = Ping(PingRole.Router, 50) });

        Assert.Contains(NetworkFindingKind.InternetLossProvider, Kinds(provider));
        Assert.DoesNotContain(NetworkFindingKind.InternetLossLocal, Kinds(provider));
        Assert.Contains(NetworkFindingKind.InternetLossLocal, Kinds(local));
        Assert.Equal(NetworkVerdict.Warning, provider.Verdict);
    }

    [Fact]
    public void MoreThanFivePercentLossMakesTheConnectionBad()
    {
        var lossy = Healthy().Internet.Select(p => p with { Received = 90 }).ToList();

        Assert.Equal(NetworkVerdict.Bad, NetworkEvaluator.Evaluate(Healthy() with { Internet = lossy }).Verdict);
    }

    [Fact]
    public void HighLatencyIsAWarningAndVeryHighLatencyIsBad()
    {
        var slow = Healthy() with { Internet = [Ping(PingRole.InternetPrimary, 100), Ping(PingRole.InternetSecondary, 100)] };
        var awful = Healthy() with { Internet = [Ping(PingRole.InternetPrimary, 200), Ping(PingRole.InternetSecondary, 200)] };

        var warning = NetworkEvaluator.Evaluate(slow);

        Assert.Equal(NetworkVerdict.Warning, warning.Verdict);
        Assert.Equal(new NetworkFinding(NetworkFindingKind.HighLatencyProvider, 100), Assert.Single(warning.Findings));
        Assert.Equal(NetworkVerdict.Bad, NetworkEvaluator.Evaluate(awful).Verdict);
    }

    [Fact]
    public void JitterUsesTheWorstAnsweringServer()
    {
        var shaky = Healthy() with { Internet = [Ping(PingRole.InternetPrimary, 12, jitter: 5), Ping(PingRole.InternetSecondary, 14, jitter: 35)] };

        var assessment = NetworkEvaluator.Evaluate(shaky);

        Assert.Equal(new NetworkFinding(NetworkFindingKind.HighJitter, 35), Assert.Single(assessment.Findings));
    }

    [Fact]
    public void NoInternetAnswerIsBadAndHasNoLatencyFindings()
    {
        var offline = Healthy() with { Internet = [Ping(PingRole.InternetPrimary, 0, answered: false), Ping(PingRole.InternetSecondary, 0, answered: false)] };

        var assessment = NetworkEvaluator.Evaluate(offline);

        Assert.Equal(NetworkVerdict.Bad, assessment.Verdict);
        Assert.Equal([NetworkFindingKind.InternetUnreachable], Kinds(assessment));
    }

    [Fact]
    public void LatencyThatClimbsUnderLoadIsBufferbloat()
    {
        var assessment = NetworkEvaluator.Evaluate(Healthy() with { LoadedLatencyMs = 180 });

        Assert.Equal(NetworkVerdict.Warning, assessment.Verdict);
        Assert.Equal(new NetworkFinding(NetworkFindingKind.Bufferbloat, 180), Assert.Single(assessment.Findings));
    }

    [Fact]
    public void ASmallRiseUnderLoadIsNotBufferbloat()
    {
        Assert.Empty(NetworkEvaluator.Evaluate(Healthy() with { LoadedLatencyMs = 90 }).Findings);
    }

    [Fact]
    public void SlowDnsAndLowDownloadAreReportedWithTheirValues()
    {
        var assessment = NetworkEvaluator.Evaluate(Healthy() with { DnsMilliseconds = 400, DownloadMbps = 4.5 });

        Assert.Equal(
            [new NetworkFinding(NetworkFindingKind.SlowDns, 400), new NetworkFinding(NetworkFindingKind.LowDownload, 4.5)],
            assessment.Findings);
    }

    [Fact]
    public void AFailedSpeedTestIsReportedOnlyWhenThereIsNoDownloadNumber()
    {
        var failed = NetworkEvaluator.Evaluate(Healthy() with { DownloadMbps = null, UploadMbps = null, SpeedTestFailed = true });
        var partial = NetworkEvaluator.Evaluate(Healthy() with { UploadMbps = null, SpeedTestFailed = true });

        Assert.Equal([NetworkFindingKind.SpeedTestUnavailable], Kinds(failed));
        Assert.Empty(partial.Findings);
    }

    [Fact]
    public void ThresholdsSitExactlyOnTheLimitsWithoutTriggering()
    {
        var atLimit = Healthy() with
        {
            Router = Ping(PingRole.Router, NetworkEvaluator.RouterLatencyMs, jitter: NetworkEvaluator.RouterJitterMs),
            Internet = [Ping(PingRole.InternetPrimary, NetworkEvaluator.InternetLatencyMs, jitter: NetworkEvaluator.InternetJitterMs)],
            DnsMilliseconds = NetworkEvaluator.SlowDnsMs,
            DownloadMbps = NetworkEvaluator.LowDownloadMbps,
        };

        Assert.Empty(NetworkEvaluator.Evaluate(atLimit).Findings);
    }
}
