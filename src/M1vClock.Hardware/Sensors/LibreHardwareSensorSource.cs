using System.Net.NetworkInformation;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Sensors;

public interface ISensorSource : IDisposable
{
    void Open();

    IReadOnlyList<SensorReading> Read();

    IReadOnlyList<SensorReading> ReadComponent(SensorHardware component);
}

public sealed class LibreHardwareSensorSource : ISensorSource
{
    private readonly ILogger<LibreHardwareSensorSource> _logger;
    private bool _networkFailureLogged;

    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsMotherboardEnabled = true,
        IsStorageEnabled = true,
        IsNetworkEnabled = true,
        IsControllerEnabled = true,
        IsPsuEnabled = true,
        IsBatteryEnabled = true,
    };

    private bool _opened;

    public LibreHardwareSensorSource(ILogger<LibreHardwareSensorSource>? logger = null)
    {
        _logger = logger ?? NullLogger<LibreHardwareSensorSource>.Instance;
    }

    public static SensorHardware MapHardware(HardwareType type) => type switch
    {
        HardwareType.Cpu => SensorHardware.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => SensorHardware.Gpu,
        HardwareType.Memory => SensorHardware.Memory,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => SensorHardware.Motherboard,
        HardwareType.Storage => SensorHardware.Storage,
        HardwareType.Network => SensorHardware.Network,
        HardwareType.Cooler => SensorHardware.Cooling,
        HardwareType.Psu or HardwareType.PowerMonitor => SensorHardware.PowerSupply,
        HardwareType.Battery => SensorHardware.Battery,
        _ => SensorHardware.Other,
    };

    public static SensorKind MapKind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Power => SensorKind.Power,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Current => SensorKind.Current,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Control => SensorKind.Control,
        SensorType.Level => SensorKind.Level,
        SensorType.Data => SensorKind.Data,
        SensorType.SmallData => SensorKind.SmallData,
        SensorType.Throughput => SensorKind.Throughput,
        SensorType.Frequency => SensorKind.Frequency,
        SensorType.Energy => SensorKind.Energy,
        SensorType.Flow => SensorKind.Flow,
        SensorType.Humidity => SensorKind.Humidity,
        SensorType.Factor => SensorKind.Factor,
        SensorType.TimeSpan or SensorType.Timing => SensorKind.Duration,
        SensorType.Noise => SensorKind.Noise,
        SensorType.Conductivity => SensorKind.Conductivity,
        _ => SensorKind.Other,
    };

    public void Open()
    {
        _computer.Open();
        _opened = true;
    }

    public IReadOnlyList<SensorReading> Read()
    {
        if (!_opened)
        {
            throw new InvalidOperationException("Os sensores ainda não foram abertos.");
        }

        var readings = new List<SensorReading>();
        var interfaces = ReadInterfaces();
        foreach (var hardware in _computer.Hardware)
        {
            if (hardware.HardwareType == HardwareType.Network && !NetworkSensorFilter.IsConnectedAdapter(hardware.Name, interfaces))
            {
                continue;
            }

            Walk(hardware, readings);
        }

        return readings;
    }

    public IReadOnlyList<SensorReading> ReadComponent(SensorHardware component)
    {
        if (!_opened)
        {
            throw new InvalidOperationException("Os sensores ainda não foram abertos.");
        }

        var readings = new List<SensorReading>();
        foreach (var hardware in _computer.Hardware)
        {
            if (MapHardware(hardware.HardwareType) == component)
            {
                Walk(hardware, readings);
            }
        }

        return readings;
    }

    public void Dispose()
    {
        if (_opened)
        {
            _computer.Close();
            _opened = false;
        }
    }

    private IReadOnlyList<NetworkAdapterState> ReadInterfaces()
    {
        try
        {
            return [.. NetworkInterface.GetAllNetworkInterfaces().Select(n => new NetworkAdapterState(n.Name, n.OperationalStatus, n.NetworkInterfaceType, HasUsableAddress(n)))];
        }
        catch (NetworkInformationException ex)
        {
            LogNetworkFailure(ex);
            return [];
        }
    }

    private void LogNetworkFailure(Exception exception)
    {
        if (_networkFailureLogged)
        {
            return;
        }

        _networkFailureLogged = true;
        HardwareLog.OptionalSourceFailed(_logger, exception, "adaptadores de rede dos sensores");
    }

    private bool HasUsableAddress(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().UnicastAddresses.Any(a =>
                a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                && !System.Net.IPAddress.IsLoopback(a.Address)
                && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
        }
        catch (NetworkInformationException ex)
        {
            LogNetworkFailure(ex);
            return false;
        }
    }

    private static void Walk(IHardware hardware, List<SensorReading> readings)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            readings.Add(new SensorReading(
                sensor.Identifier.ToString(),
                hardware.Name,
                MapHardware(hardware.HardwareType),
                sensor.Name,
                MapKind(sensor.SensorType),
                sensor.Value,
                sensor.Min,
                sensor.Max));
        }

        foreach (var sub in hardware.SubHardware)
        {
            Walk(sub, readings);
        }
    }
}
