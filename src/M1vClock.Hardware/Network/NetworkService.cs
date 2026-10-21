using System.Net;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Network;

public sealed record NetworkTestOptions(int PingCount, TimeSpan PingInterval, TimeSpan DownloadDuration, TimeSpan UploadDuration)
{
    public static NetworkTestOptions Default { get; } = new(30, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(6));
}

public interface INetworkService
{
    Task<ReadResult<NetworkOverview>> ReadAsync(CancellationToken cancellationToken);

    Task<NetworkTestReport?> RunTestAsync(IProgress<NetworkProgress>? progress, CancellationToken cancellationToken);
}

public sealed class NetworkService(
    INetworkInterfaceSource interfaces,
    INetworkProbe probe,
    NetworkTestOptions options,
    TimeProvider clock,
    ILogger<NetworkService> logger) : INetworkService
{
    public const string DnsProbeHost = "www.cloudflare.com";
    public const int MinimumLoadedSamples = 5;

    private static readonly IPAddress PrimaryTarget = IPAddress.Parse("1.1.1.1");
    private static readonly IPAddress SecondaryTarget = IPAddress.Parse("1.0.0.1");

    public Task<ReadResult<NetworkOverview>> ReadAsync(CancellationToken cancellationToken) =>
        SectionReader.ReadAsync(logger, "rede", () => NetworkAdapterSelector.Overview(interfaces.Read()), cancellationToken);

    public async Task<NetworkTestReport?> RunTestAsync(IProgress<NetworkProgress>? progress, CancellationToken cancellationToken)
    {
        Report(progress, NetworkStage.FindingConnection, 0.02);
        var overview = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!overview.TryGet(out var read) || read.Active is not { } adapter)
        {
            HardwareLog.NoActiveAdapter(logger);
            return null;
        }

        if (!IPAddress.TryParse(adapter.Gateway, out var gateway))
        {
            throw new HardwareReadException("O endereço do roteador não é um IPv4 válido.");
        }

        Report(progress, NetworkStage.Latency, 0.08);
        var pings = await MeasureLatencyAsync(gateway, cancellationToken).ConfigureAwait(false);

        Report(progress, NetworkStage.Dns, 0.30);
        var dns = await probe.ResolveAsync(DnsProbeHost, cancellationToken).ConfigureAwait(false);

        Report(progress, NetworkStage.Download, 0.36);
        var (download, loaded) = await MeasureDownloadUnderLoadAsync(cancellationToken).ConfigureAwait(false);

        Report(progress, NetworkStage.Upload, 0.66);
        var upload = await probe.MeasureUploadAsync(options.UploadDuration, cancellationToken).ConfigureAwait(false);

        Report(progress, NetworkStage.Analyzing, 0.95);
        var measurements = new NetworkMeasurements(pings[0], [pings[1], pings[2]], dns, download, upload, loaded, SpeedTestFailed: download is null);
        var report = new NetworkTestReport(adapter, measurements, NetworkEvaluator.Evaluate(measurements), clock.GetUtcNow());
        Report(progress, NetworkStage.Done, 1);
        return report;
    }

    private static void Report(IProgress<NetworkProgress>? progress, NetworkStage stage, double fraction) =>
        progress?.Report(new NetworkProgress(stage, fraction));

    private async Task<PingStats[]> MeasureLatencyAsync(IPAddress gateway, CancellationToken cancellationToken)
    {
        (PingRole Role, IPAddress Address)[] targets =
        [
            (PingRole.Router, gateway),
            (PingRole.InternetPrimary, PrimaryTarget),
            (PingRole.InternetSecondary, SecondaryTarget),
        ];
        var series = await Task.WhenAll(targets.Select(t => probe.PingAsync(t.Address, options.PingCount, options.PingInterval, cancellationToken))).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return [.. targets.Select((t, i) => PingStatistics.Summarize(t.Role, t.Address.ToString(), series[i].RoundTrips, series[i].Sent))];
    }

    private async Task<(double? Download, double? LoadedLatency)> MeasureDownloadUnderLoadAsync(CancellationToken cancellationToken)
    {
        using var stopLoadedPing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loadedPing = probe.PingAsync(PrimaryTarget, 0, options.PingInterval, stopLoadedPing.Token);
        double? download;
        PingSeries series;
        try
        {
            download = await probe.MeasureDownloadAsync(options.DownloadDuration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stopLoadedPing.CancelAsync().ConfigureAwait(false);
            series = await loadedPing.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (download, series.RoundTrips.Count >= MinimumLoadedSamples ? series.RoundTrips.Average() : null);
    }
}
