using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Network;

public sealed class SystemNetworkProbe : INetworkProbe, IDisposable
{
    private const int DownloadWorkers = 4;
    private const int UploadWorkers = 3;
    private const int PingTimeoutMs = 1500;
    private const int ReadBufferBytes = 81_920;

    private readonly ILogger<SystemNetworkProbe> _logger;
    private readonly HttpClient _http;

    public SystemNetworkProbe(ILogger<SystemNetworkProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public void Dispose() => _http.Dispose();

    public async Task<PingSeries> PingAsync(IPAddress address, int count, TimeSpan interval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        var roundTrips = new List<double>();
        var sent = 0;
        var failures = 0;
        Exception? lastFailure = null;
        using var ping = new Ping();
        for (var i = 0; (count <= 0 || i < count) && !cancellationToken.IsCancellationRequested; i++)
        {
            var clock = Stopwatch.StartNew();
            sent++;
            try
            {
                var reply = await ping.SendPingAsync(address, PingTimeoutMs).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    roundTrips.Add(Math.Max(1, reply.RoundtripTime));
                }
            }
            catch (Exception ex) when (ex is PingException or InvalidOperationException or SocketException)
            {
                failures++;
                lastFailure = ex;
            }

            if (!await WaitAsync(interval - clock.Elapsed, cancellationToken).ConfigureAwait(false))
            {
                break;
            }
        }

        if (lastFailure is not null)
        {
            HardwareLog.PingFailed(_logger, lastFailure, address.ToString(), failures);
        }

        return new PingSeries(roundTrips, sent);
    }

    public async Task<double?> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        try
        {
            var clock = Stopwatch.StartNew();
            await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            return clock.Elapsed.TotalMilliseconds;
        }
        catch (SocketException ex)
        {
            HardwareLog.DnsFailed(_logger, ex, host);
            return null;
        }
    }

    public Task<double?> MeasureDownloadAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        MeasureAsync("download", DownloadWorkers, duration, DownloadLoopAsync, cancellationToken);

    public Task<double?> MeasureUploadAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var payload = new byte[SpeedTestEndpoints.UploadBytes];
        RandomNumberGenerator.Fill(payload);
        return MeasureAsync("upload", UploadWorkers, duration, (counter, token) => UploadLoopAsync(payload, counter, token), cancellationToken);
    }

    private static async Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<double?> MeasureAsync(string direction, int workers, TimeSpan duration, Func<ByteCounter, CancellationToken, Task> loop, CancellationToken cancellationToken)
    {
        var counter = new ByteCounter();
        var clock = Stopwatch.StartNew();
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(duration);
        var tasks = Enumerable.Range(0, workers).Select(_ => RunWorkerAsync(direction, loop, counter, window.Token)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var seconds = clock.Elapsed.TotalSeconds;
        return counter.Total > 0 && seconds > 0 ? counter.Total * 8 / 1e6 / seconds : null;
    }

    private async Task RunWorkerAsync(string direction, Func<ByteCounter, CancellationToken, Task> loop, ByteCounter counter, CancellationToken cancellationToken)
    {
        try
        {
            await loop(counter, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or HardwareReadException or IOException)
        {
            HardwareLog.SpeedTestFailed(_logger, ex, direction);
        }
    }

    private async Task DownloadLoopAsync(ByteCounter counter, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReadBufferBytes];
        while (!cancellationToken.IsCancellationRequested)
        {
            using var response = await _http.GetAsync(SpeedTestEndpoints.Require(SpeedTestEndpoints.Download), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length > SpeedTestEndpoints.DownloadBytes)
            {
                throw new HardwareReadException("O servidor de velocidade enviou mais bytes do que o pedido.");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                long received = 0;
                int read;
                while (received < SpeedTestEndpoints.DownloadBytes && (read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += read;
                    counter.Add(read);
                }
            }
        }
    }

    private async Task UploadLoopAsync(byte[] payload, ByteCounter counter, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var content = new ByteArrayContent(payload);
            using var response = await _http.PostAsync(SpeedTestEndpoints.Require(SpeedTestEndpoints.Upload), content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            counter.Add(payload.Length);
        }
    }

    private sealed class ByteCounter
    {
        private long _total;

        public long Total => Interlocked.Read(ref _total);

        public void Add(long bytes) => Interlocked.Add(ref _total, bytes);
    }
}
