using System.Net;
using System.Net.Http;
using M1vClock.Hardware.Network;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Tests;

public sealed class SpeedTestLimitsTests
{
    private sealed class ZeroStream(long length, Action<int> onRead, TimeSpan? delayPerRead = null) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (delayPerRead is { } delay)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            var count = (int)Math.Min(buffer.Length, length - _position);
            buffer.Span[..count].Clear();
            _position += count;
            onRead(count);
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private long _served;
        private int _requests;

        public long ServedBytes => Interlocked.Read(ref _served);

        public int Requests => Volatile.Read(ref _requests);

        public List<Uri> Uris { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public long? DeclaredLength { get; set; }

        public TimeSpan? DelayPerRead { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            lock (Uris)
            {
                Uris.Add(request.RequestUri!);
            }

            var response = new HttpResponseMessage(Status);
            if (request.Method == HttpMethod.Get)
            {
                response.Content = new StreamContent(new ZeroStream(SpeedTestEndpoints.DownloadBytes, count => Interlocked.Add(ref _served, count), DelayPerRead));
                if (DeclaredLength is { } length)
                {
                    response.Content.Headers.ContentLength = length;
                }
            }

            return Task.FromResult(response);
        }
    }

    private static SystemNetworkProbe Create(FakeHandler handler, CollectingLogger<SystemNetworkProbe>? logger = null) =>
        new(logger ?? new CollectingLogger<SystemNetworkProbe>(), handler);

    [Fact]
    public async Task TheDownloadStopsAtTheDataCapInsteadOfRunningForTheWholeDuration()
    {
        var handler = new FakeHandler();
        using var probe = Create(handler);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var mbps = await probe.MeasureDownloadAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.True(mbps > 0);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "devia parar no limite de dados, não nos 30 s");
        Assert.InRange(handler.ServedBytes, SystemNetworkProbe.DownloadCapBytes, SystemNetworkProbe.DownloadCapBytes + 4 * SpeedTestEndpoints.DownloadBytes);
    }

    [Fact]
    public async Task TheUploadStopsAtItsOwnDataCap()
    {
        var handler = new FakeHandler();
        using var probe = Create(handler);

        var mbps = await probe.MeasureUploadAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.True(mbps > 0);
        var posted = (long)handler.Requests * SpeedTestEndpoints.UploadBytes;
        Assert.InRange(posted, SystemNetworkProbe.UploadCapBytes, SystemNetworkProbe.UploadCapBytes + 6 * SpeedTestEndpoints.UploadBytes);
    }

    [Fact]
    public async Task ASlowLinkStopsAtTheTimeLimitBelowTheCap()
    {
        var handler = new FakeHandler { DelayPerRead = TimeSpan.FromMilliseconds(40) };
        using var probe = Create(handler);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var mbps = await probe.MeasureDownloadAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.InRange(clock.Elapsed, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(5));
        Assert.True(mbps > 0);
        Assert.True(handler.ServedBytes < SystemNetworkProbe.DownloadCapBytes);
    }

    [Fact]
    public async Task EveryRequestGoesToTheAllowedHostOverHttps()
    {
        var handler = new FakeHandler();
        using var probe = Create(handler);

        await probe.MeasureDownloadAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await probe.MeasureUploadAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.All(handler.Uris, uri => Assert.True(SpeedTestEndpoints.IsAllowed(uri), uri.ToString()));
        Assert.Contains(handler.Uris, u => u.AbsolutePath == "/__down");
        Assert.Contains(handler.Uris, u => u.AbsolutePath == "/__up");
    }

    [Fact]
    public async Task AServerThatDeclaresMoreBytesThanAskedIsRefusedAndLogged()
    {
        var handler = new FakeHandler { DeclaredLength = SpeedTestEndpoints.DownloadBytes + 1 };
        var logger = new CollectingLogger<SystemNetworkProbe>();
        using var probe = Create(handler, logger);

        var mbps = await probe.MeasureDownloadAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Null(mbps);
        Assert.Equal(0, handler.ServedBytes);
        Assert.Contains(logger.Entries, e => e.EventId.Id == 3103 && e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ARedirectOrAnErrorStatusGivesNoMeasurementInsteadOfFollowingOrCounting(HttpStatusCode status)
    {
        var handler = new FakeHandler { Status = status };
        using var probe = Create(handler);

        Assert.Null(await probe.MeasureDownloadAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Null(await probe.MeasureUploadAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancellingByTheUserStopsTheMeasurementWithACancellation()
    {
        var handler = new FakeHandler { DelayPerRead = TimeSpan.FromMilliseconds(40) };
        using var probe = Create(handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.MeasureDownloadAsync(TimeSpan.FromSeconds(30), cancellation.Token));
    }

    [Fact]
    public void TheCapsKeepTheTestSmallForMeteredConnections()
    {
        Assert.True(SystemNetworkProbe.DownloadCapBytes + SystemNetworkProbe.UploadCapBytes <= 400_000_000);
    }
}
