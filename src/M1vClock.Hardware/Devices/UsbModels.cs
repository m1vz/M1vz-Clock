namespace M1vClock.Hardware.Devices;

public enum UsbCategory
{
    SerialPort,
    Audio,
    Input,
    Storage,
    Imaging,
    Bluetooth,
    Network,
    Generic,
    Hub,
}

public sealed record PnpEntry(string DeviceId, string Name, string PnpClass, uint ProblemCode);

public sealed record UsbDeviceInfo(
    string InstanceId,
    string Name,
    string PnpClass,
    UsbCategory Category,
    string? Vid,
    string? Pid,
    string? Maker,
    string? ComPort,
    uint? ProblemCode)
{
    public string? HardwareId => Vid is null || Pid is null ? null : $"{Vid}:{Pid}";

    public bool HasProblem => ProblemCode is > 0;
}

public enum DeviceChangeKind
{
    Connected,
    Disconnected,
}

public sealed record DeviceChange(DeviceChangeKind Kind, UsbDeviceInfo Device, DateTimeOffset At);

public sealed record VolumeEvent(string DriveLetter, bool Arrived, DateTimeOffset At);

public sealed record DeviceDiff(IReadOnlyList<UsbDeviceInfo> Added, IReadOnlyList<UsbDeviceInfo> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;
}
