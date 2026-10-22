using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Network;

public interface INetworkInterfaceSource
{
    IReadOnlyList<InterfaceSnapshot> Read();
}

public sealed class SystemNetworkInterfaceSource(ILogger<SystemNetworkInterfaceSource> logger) : INetworkInterfaceSource
{
    public IReadOnlyList<InterfaceSnapshot> Read()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException ex)
        {
            throw new HardwareReadException("O Windows não listou os adaptadores de rede.", ex);
        }

        var snapshots = new List<InterfaceSnapshot>();
        foreach (var network in interfaces)
        {
            var snapshot = Snapshot(network);
            if (snapshot is not null)
            {
                snapshots.Add(snapshot);
            }
        }

        return snapshots;
    }

    private static string[] Ipv4Of<T>(IEnumerable<T> items, Func<T, IPAddress> address) =>
        [.. items.Select(address).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString())];

    private static string? Mac(NetworkInterface network)
    {
        var bytes = network.GetPhysicalAddress().GetAddressBytes();
        return bytes.Length == 0 ? null : string.Join('-', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private InterfaceSnapshot? Snapshot(NetworkInterface network)
    {
        try
        {
            var properties = network.GetIPProperties();
            return new InterfaceSnapshot(
                network.Name,
                network.Description,
                network.NetworkInterfaceType,
                network.OperationalStatus,
                network.Speed,
                Ipv4Of(properties.UnicastAddresses, a => a.Address),
                Ipv4Of(properties.GatewayAddresses, a => a.Address),
                Ipv4Of(properties.DnsAddresses, a => a),
                Mac(network));
        }
        catch (NetworkInformationException ex)
        {
            HardwareLog.OptionalSourceFailed(logger, ex, $"adaptador {network.Name}");
            return null;
        }
    }
}
