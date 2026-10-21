using System.Net.NetworkInformation;
using M1vClock.Core.Privacy;

namespace M1vClock.Hardware.Network;

public sealed record InterfaceSnapshot(
    string Name,
    string Description,
    NetworkInterfaceType Type,
    OperationalStatus Status,
    long SpeedBitsPerSecond,
    IReadOnlyList<string> Ipv4Addresses,
    IReadOnlyList<string> Ipv4Gateways,
    IReadOnlyList<string> DnsServers,
    string? MacAddress);

public static class NetworkAdapterSelector
{
    private const string UnspecifiedGateway = "0.0.0.0";

    public static NetworkAdapterKind Classify(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => NetworkAdapterKind.Wifi,
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => NetworkAdapterKind.Ethernet,
        _ => NetworkAdapterKind.Other,
    };

    public static NetworkOverview Overview(IReadOnlyList<InterfaceSnapshot> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        var connected = interfaces.Where(IsConnected).ToList();
        var active = connected
            .Where(i => GatewayOf(i) is not null)
            .OrderByDescending(i => i.SpeedBitsPerSecond)
            .FirstOrDefault();
        var others = connected
            .Where(i => i != active && i.Ipv4Addresses.Count > 0)
            .OrderByDescending(i => i.SpeedBitsPerSecond)
            .Select(ToAdapter)
            .ToList();
        return new NetworkOverview(active is null ? null : ToAdapter(active), others);
    }

    public static NetworkAdapter ToAdapter(InterfaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new NetworkAdapter(
            snapshot.Name,
            snapshot.Description,
            Classify(snapshot.Type),
            Math.Max(0, snapshot.SpeedBitsPerSecond),
            snapshot.Ipv4Addresses.Count > 0 ? snapshot.Ipv4Addresses[0] : null,
            GatewayOf(snapshot),
            snapshot.DnsServers,
            new SensitiveText(snapshot.MacAddress));
    }

    private static bool IsConnected(InterfaceSnapshot snapshot) =>
        snapshot.Status == OperationalStatus.Up && snapshot.Type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel);

    private static string? GatewayOf(InterfaceSnapshot snapshot) =>
        snapshot.Ipv4Gateways.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g) && g != UnspecifiedGateway);
}
