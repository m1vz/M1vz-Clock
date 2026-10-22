using System.Net;
using M1vClock.Hardware.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class SpeedTestEndpointsTests
{
    [Fact]
    public void TheOfficialEndpointsAreAllowedAndUseHttps()
    {
        Assert.True(SpeedTestEndpoints.IsAllowed(SpeedTestEndpoints.Download));
        Assert.True(SpeedTestEndpoints.IsAllowed(SpeedTestEndpoints.Upload));
        Assert.Equal(Uri.UriSchemeHttps, SpeedTestEndpoints.Download.Scheme);
        Assert.Equal("25000000", SpeedTestEndpoints.Download.Query.Split('=')[1]);
    }

    [Theory]
    [InlineData("http://speed.cloudflare.com/__down?bytes=1")]
    [InlineData("https://speed.cloudflare.com:8443/__down?bytes=1")]
    [InlineData("https://speed.cloudflare.com.example.com/__down?bytes=1")]
    [InlineData("https://example.com/__down?bytes=1")]
    [InlineData("ftp://speed.cloudflare.com/file")]
    public void AnythingElseIsRejected(string address)
    {
        var uri = new Uri(address);

        Assert.False(SpeedTestEndpoints.IsAllowed(uri));
        Assert.Throws<HardwareReadException>(() => SpeedTestEndpoints.Require(uri));
    }

    [Fact]
    public void ARelativeAddressIsRejected()
    {
        Assert.False(SpeedTestEndpoints.IsAllowed(new Uri("/__down", UriKind.Relative)));
    }

    [Fact]
    public void TheHostIsComparedWithoutCase()
    {
        Assert.True(SpeedTestEndpoints.IsAllowed(new Uri("https://SPEED.Cloudflare.com/__up")));
    }
}

public sealed class SystemNetworkProbeTests
{
    private static SystemNetworkProbe Create() => new(NullLogger<SystemNetworkProbe>.Instance);

    [Fact]
    public async Task PingingTheLoopbackAnswersEveryPacket()
    {
        using var probe = Create();

        var series = await probe.PingAsync(IPAddress.Loopback, 4, TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);

        Assert.Equal(4, series.Sent);
        Assert.Equal(4, series.RoundTrips.Count);
        Assert.All(series.RoundTrips, rtt => Assert.True(rtt >= 1));
    }

    [Fact]
    public async Task CancellingAnOpenEndedPingReturnsWhatWasCollected()
    {
        using var probe = Create();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromMilliseconds(250));

        var series = await probe.PingAsync(IPAddress.Loopback, 0, TimeSpan.FromMilliseconds(30), stop.Token);

        Assert.InRange(series.Sent, 2, 20);
        Assert.Equal(series.Sent, series.RoundTrips.Count);
    }

    [Fact]
    public async Task ResolvingLocalhostReturnsAPositiveTime()
    {
        using var probe = Create();

        var milliseconds = await probe.ResolveAsync("localhost", TestContext.Current.CancellationToken);

        Assert.True(milliseconds >= 0);
    }

    [Fact]
    public async Task AnUnknownNameResolvesToNothingInsteadOfFailing()
    {
        using var probe = Create();

        var milliseconds = await probe.ResolveAsync("nome-que-nao-existe.invalid", TestContext.Current.CancellationToken);

        Assert.Null(milliseconds);
    }

    [Fact]
    public async Task ABlankHostIsRejected()
    {
        using var probe = Create();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => probe.ResolveAsync(" ", TestContext.Current.CancellationToken));
    }
}
