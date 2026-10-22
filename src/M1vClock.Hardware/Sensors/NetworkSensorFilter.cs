using System.Net.NetworkInformation;

namespace M1vClock.Hardware.Sensors;

public sealed record NetworkAdapterState(string Name, OperationalStatus Status, NetworkInterfaceType Type, bool HasUsableAddress);

public static class NetworkSensorFilter
{
    public static bool IsConnectedAdapter(string hardwareName, IReadOnlyList<NetworkAdapterState> interfaces)
    {
        ArgumentNullException.ThrowIfNull(hardwareName);
        ArgumentNullException.ThrowIfNull(interfaces);
        if (interfaces.Count == 0)
        {
            return true;
        }

        return interfaces.Any(i =>
            string.Equals(i.Name, hardwareName, StringComparison.OrdinalIgnoreCase)
            && i.Status == OperationalStatus.Up
            && i.HasUsableAddress
            && i.Type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel));
    }
}
