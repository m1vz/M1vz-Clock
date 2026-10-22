using System.Net;
using System.Net.NetworkInformation;
using M1vClock.Hardware.Network;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Tests;

internal sealed class FakeInterfaceSource(params InterfaceSnapshot[] interfaces) : INetworkInterfaceSource
{
    public Exception? Failure { get; init; }

    public IReadOnlyList<InterfaceSnapshot> Read() => Failure is null ? interfaces : throw Failure;
}

internal sealed class FakeProbe : INetworkProbe
{
    private readonly Dictionary<string, PingSeries> _series = new(StringComparer.Ordinal);

    public Dictionary<string, PingSeries> Series => _series;

    public PingSeries DefaultSeries { get; set; } = Steady(10, 30);

    public PingSeries LoadedSeries { get; set; } = Steady(12, 20);

    public double? Dns { get; set; } = 25;

    public double? Download { get; set; } = 300;

    public double? Upload { get; set; } = 90;

    public Action? OnDownload { get; set; }

    public List<string> PingedAddresses { get; } = [];

    public List<string> ResolvedHosts { get; } = [];

    public Task? LoadedPingTask { get; private set; }

    public static PingSeries Steady(double rtt, int count) => new(Enumerable.Repeat(rtt, count).ToList(), count);

    public async Task<PingSeries> PingAsync(IPAddress address, int count, TimeSpan interval, CancellationToken cancellationToken)
    {
        PingedAddresses.Add(address.ToString());
        if (count > 0)
        {
            return _series.TryGetValue(address.ToString(), out var series) ? series : DefaultSeries;
        }

        var completion = new TaskCompletionSource();
        LoadedPingTask = completion.Task;
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            completion.SetResult();
        }

        return LoadedSeries;
    }

    public Task<double?> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        ResolvedHosts.Add(host);
        return Task.FromResult(Dns);
    }

    public async Task<double?> MeasureDownloadAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        OnDownload?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        return Download;
    }

    public Task<double?> MeasureUploadAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.FromResult(Upload);
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class CollectingProgress : IProgress<NetworkProgress>
{
    public List<NetworkProgress> Reports { get; } = [];

    public void Report(NetworkProgress value) => Reports.Add(value);
}

public sealed class NetworkServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 15, 30, 0, TimeSpan.Zero);
    private static readonly NetworkTestOptions QuickOptions = new(5, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));

    private static InterfaceSnapshot Ethernet(string[]? gateways = null) => new(
        "Ethernet", "Realtek", NetworkInterfaceType.Ethernet, OperationalStatus.Up, 1_000_000_000, ["192.168.0.10"], gateways ?? ["192.168.0.1"], ["1.1.1.1"], "AA-BB-CC-DD-EE-FF");

    private static NetworkService Create(FakeProbe probe, INetworkInterfaceSource? source = null, CollectingLogger<NetworkService>? logger = null) =>
        new(source ?? new FakeInterfaceSource(Ethernet()), probe, QuickOptions, new FixedTimeProvider(Now), logger ?? new CollectingLogger<NetworkService>());

    [Fact]
    public async Task WithoutAnActiveAdapterNothingIsMeasured()
    {
        var probe = new FakeProbe();
        var progress = new CollectingProgress();
        var service = Create(probe, new FakeInterfaceSource());

        var report = await service.RunTestAsync(progress, TestContext.Current.CancellationToken);

        Assert.Null(report);
        Assert.Empty(probe.PingedAddresses);
        Assert.Equal([NetworkStage.FindingConnection], progress.Reports.Select(r => r.Stage));
    }

    [Fact]
    public async Task ACompleteRunMeasuresTheRouterAndBothCloudflareResolvers()
    {
        var probe = new FakeProbe();
        var service = Create(probe);

        var report = await service.RunTestAsync(null, TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal("Ethernet", report.Adapter.Name);
        Assert.Equal(Now, report.FinishedAt);
        Assert.Equal(NetworkVerdict.Stable, report.Assessment.Verdict);
        Assert.Equal(["192.168.0.1", "1.1.1.1", "1.0.0.1"], probe.PingedAddresses.Take(3));
        Assert.Equal([PingRole.Router, PingRole.InternetPrimary, PingRole.InternetSecondary], new[] { report.Measurements.Router }.Concat(report.Measurements.Internet).Select(p => p.Role));
        Assert.Equal([NetworkService.DnsProbeHost], probe.ResolvedHosts);
        Assert.Equal(25, report.Measurements.DnsMilliseconds);
        Assert.Equal(300, report.Measurements.DownloadMbps);
        Assert.Equal(90, report.Measurements.UploadMbps);
        Assert.False(report.Measurements.SpeedTestFailed);
        Assert.DoesNotContain("8.8.8.8", probe.PingedAddresses);
    }

    [Fact]
    public async Task ProgressWalksThroughEveryStageAndOnlyMovesForward()
    {
        var progress = new CollectingProgress();

        await Create(new FakeProbe()).RunTestAsync(progress, TestContext.Current.CancellationToken);

        Assert.Equal(
            [NetworkStage.FindingConnection, NetworkStage.Latency, NetworkStage.Dns, NetworkStage.Download, NetworkStage.Upload, NetworkStage.Analyzing, NetworkStage.Done],
            progress.Reports.Select(r => r.Stage));
        Assert.Equal(progress.Reports.Select(r => r.Fraction).Order(), progress.Reports.Select(r => r.Fraction));
        Assert.Equal(1, progress.Reports[^1].Fraction);
    }

    [Fact]
    public async Task LatencyUnderLoadIsTheAverageOfThePingsDuringTheDownload()
    {
        var probe = new FakeProbe { LoadedSeries = FakeProbe.Steady(190, 12) };

        var report = await Create(probe).RunTestAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(190, report!.Measurements.LoadedLatencyMs);
        Assert.Contains(report.Assessment.Findings, f => f.Kind == NetworkFindingKind.Bufferbloat);
        Assert.Equal(NetworkVerdict.Warning, report.Assessment.Verdict);
    }

    [Fact]
    public async Task TooFewPingsDuringTheDownloadAreNotTrusted()
    {
        var probe = new FakeProbe { LoadedSeries = FakeProbe.Steady(500, NetworkService.MinimumLoadedSamples - 1) };

        var report = await Create(probe).RunTestAsync(null, TestContext.Current.CancellationToken);

        Assert.Null(report!.Measurements.LoadedLatencyMs);
    }

    [Fact]
    public async Task AFailedDownloadIsReportedAsSpeedTestUnavailable()
    {
        var probe = new FakeProbe { Download = null, Upload = null };

        var report = await Create(probe).RunTestAsync(null, TestContext.Current.CancellationToken);

        Assert.True(report!.Measurements.SpeedTestFailed);
        Assert.Contains(report.Assessment.Findings, f => f.Kind == NetworkFindingKind.SpeedTestUnavailable);
    }

    [Fact]
    public async Task ANetworkThatAnswersNothingIsBad()
    {
        var silent = new PingSeries([], 5);
        var probe = new FakeProbe { DefaultSeries = silent };

        var report = await Create(probe).RunTestAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(NetworkVerdict.Bad, report!.Assessment.Verdict);
        Assert.Contains(report.Assessment.Findings, f => f.Kind == NetworkFindingKind.InternetUnreachable);
    }

    [Fact]
    public async Task CancellingDuringTheDownloadStopsThePingRunningAlongsideIt()
    {
        var probe = new FakeProbe();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        probe.OnDownload = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(probe).RunTestAsync(null, cancellation.Token));

        Assert.NotNull(probe.LoadedPingTask);
        await probe.LoadedPingTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AGatewayThatIsNotAnAddressStopsTheTest()
    {
        var service = Create(new FakeProbe(), new FakeInterfaceSource(Ethernet(["roteador"])));

        await Assert.ThrowsAsync<HardwareReadException>(() => service.RunTestAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadingTheOverviewReturnsTheActiveAdapter()
    {
        var result = await Create(new FakeProbe()).ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.TryGet(out var overview));
        Assert.Equal("Ethernet", overview.Active!.Name);
    }

    [Fact]
    public async Task AFailureReadingAdaptersBecomesAFailedResultAndIsLogged()
    {
        var logger = new CollectingLogger<NetworkService>();
        var source = new FakeInterfaceSource { Failure = new HardwareReadException("O Windows não listou os adaptadores de rede.") };

        var result = await Create(new FakeProbe(), source, logger).ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("O Windows não listou os adaptadores de rede.", result.Error);
        Assert.Contains(logger.Entries, e => e.EventId.Id == 3001 && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task WithoutAConnectionTheRefusalIsLogged()
    {
        var logger = new CollectingLogger<NetworkService>();

        await Create(new FakeProbe(), new FakeInterfaceSource(), logger).RunTestAsync(null, TestContext.Current.CancellationToken);

        Assert.Contains(logger.Entries, e => e.EventId.Id == 3104);
    }
}
