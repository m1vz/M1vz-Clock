using System.Net.NetworkInformation;
using M1vClock.Hardware.Network;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class NetworkAdapterSelectorTests
{
    private static InterfaceSnapshot Snapshot(
        string name,
        NetworkInterfaceType type = NetworkInterfaceType.Ethernet,
        OperationalStatus status = OperationalStatus.Up,
        long speed = 1_000_000_000,
        string[]? addresses = null,
        string[]? gateways = null,
        string[]? dns = null) => new(
            name,
            $"{name} (descrição)",
            type,
            status,
            speed,
            addresses ?? ["192.168.0.10"],
            gateways ?? ["192.168.0.1"],
            dns ?? ["1.1.1.1"],
            "AA-BB-CC-DD-EE-FF");

    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211, NetworkAdapterKind.Wifi)]
    [InlineData(NetworkInterfaceType.Ethernet, NetworkAdapterKind.Ethernet)]
    [InlineData(NetworkInterfaceType.GigabitEthernet, NetworkAdapterKind.Ethernet)]
    [InlineData(NetworkInterfaceType.FastEthernetT, NetworkAdapterKind.Ethernet)]
    [InlineData(NetworkInterfaceType.Ppp, NetworkAdapterKind.Other)]
    public void TheInterfaceTypeBecomesAFriendlyKind(NetworkInterfaceType type, NetworkAdapterKind kind)
    {
        Assert.Equal(kind, NetworkAdapterSelector.Classify(type));
    }

    [Fact]
    public void TheActiveAdapterIsTheFastestOneThatReachesAGateway()
    {
        var overview = NetworkAdapterSelector.Overview(
        [
            Snapshot("Wi-Fi", NetworkInterfaceType.Wireless80211, speed: 866_000_000),
            Snapshot("Ethernet", speed: 1_000_000_000),
            Snapshot("Virtual", speed: 10_000_000_000, gateways: []),
        ]);

        Assert.Equal("Ethernet", overview.Active!.Name);
        Assert.Equal(["Virtual", "Wi-Fi"], overview.Others.Select(o => o.Name));
    }

    [Fact]
    public void DisconnectedLoopbackAndTunnelInterfacesAreNeverActive()
    {
        var overview = NetworkAdapterSelector.Overview(
        [
            Snapshot("Cabo solto", status: OperationalStatus.Down),
            Snapshot("Loopback", NetworkInterfaceType.Loopback),
            Snapshot("Teredo", NetworkInterfaceType.Tunnel),
        ]);

        Assert.Null(overview.Active);
        Assert.Empty(overview.Others);
    }

    [Fact]
    public void AnUnspecifiedGatewayDoesNotCount()
    {
        var overview = NetworkAdapterSelector.Overview([Snapshot("Ethernet", gateways: ["0.0.0.0"])]);

        Assert.Null(overview.Active);
        Assert.Equal("Ethernet", Assert.Single(overview.Others).Name);
        Assert.Null(overview.Others[0].Gateway);
    }

    [Fact]
    public void TheAdapterCarriesItsAddressesAndTheMacStaysMasked()
    {
        var adapter = NetworkAdapterSelector.Overview([Snapshot("Ethernet", dns: ["192.168.0.1", "1.1.1.1"])]).Active!;

        Assert.Equal("192.168.0.10", adapter.Ipv4);
        Assert.Equal("192.168.0.1", adapter.Gateway);
        Assert.Equal(["192.168.0.1", "1.1.1.1"], adapter.DnsServers);
        Assert.Equal(1000, adapter.LinkMbps);
        Assert.DoesNotContain("AA-BB", adapter.MacAddress.ToString(), StringComparison.Ordinal);
        Assert.Equal("AA-BB-CC-DD-EE-FF", adapter.MacAddress.Display(reveal: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnUnknownLinkSpeedIsNotReported(long speed)
    {
        var adapter = NetworkAdapterSelector.Overview([Snapshot("Wi-Fi", speed: speed)]).Active!;

        Assert.Null(adapter.LinkMbps);
    }

    [Fact]
    public void AnAdapterWithoutAnAddressIsNotListedAsAnotherConnection()
    {
        var overview = NetworkAdapterSelector.Overview([Snapshot("Ethernet"), Snapshot("Sem IP", addresses: [], gateways: [])]);

        Assert.Empty(overview.Others);
    }

    [Fact]
    public void ThisMachinesAdaptersAreReadWithoutErrors()
    {
        var source = new SystemNetworkInterfaceSource(NullLogger<SystemNetworkInterfaceSource>.Instance);

        var snapshots = source.Read();
        var overview = NetworkAdapterSelector.Overview(snapshots);

        Assert.NotEmpty(snapshots);
        if (overview.Active is { } active)
        {
            Assert.False(string.IsNullOrWhiteSpace(active.Gateway));
            Assert.False(string.IsNullOrWhiteSpace(active.Ipv4));
        }
    }
}
