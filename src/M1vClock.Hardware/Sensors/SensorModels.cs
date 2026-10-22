namespace M1vClock.Hardware.Sensors;

public enum SensorKind
{
    Temperature,
    Load,
    Clock,
    Power,
    Voltage,
    Current,
    Fan,
    Control,
    Level,
    Data,
    SmallData,
    Throughput,
    Frequency,
    Energy,
    Flow,
    Humidity,
    Factor,
    Duration,
    Noise,
    Conductivity,
    Other,
}

public enum SensorHardware
{
    Cpu,
    Gpu,
    Memory,
    Motherboard,
    Storage,
    Network,
    Cooling,
    PowerSupply,
    Battery,
    Other,
}

public sealed record SensorReading(
    string Id,
    string HardwareName,
    SensorHardware Hardware,
    string Name,
    SensorKind Kind,
    float? Value,
    float? Min,
    float? Max);

public sealed record SensorSnapshot(DateTimeOffset At, IReadOnlyList<SensorReading> Readings)
{
    public const string PhysicalMemoryPrefix = "/ram/";

    public float? CpuTemperature
    {
        get
        {
            var temperatures = Readings.Where(r => r.Hardware == SensorHardware.Cpu && r.Kind == SensorKind.Temperature && r.Value is > 0).ToList();
            var main = temperatures.FirstOrDefault(r => HasAny(r.Name, "Package", "Tctl", "Tdie"));
            return main?.Value ?? (temperatures.Count > 0 ? temperatures.Max(r => r.Value) : null);
        }
    }

    public float? CpuLoad => Readings.FirstOrDefault(r => r.Hardware == SensorHardware.Cpu && r.Kind == SensorKind.Load && r.Name == "CPU Total")?.Value;

    public float? CpuPower => Readings.FirstOrDefault(r => r.Hardware == SensorHardware.Cpu && r.Kind == SensorKind.Power && HasAny(r.Name, "Package"))?.Value;

    public float? GpuTemperature => Readings.FirstOrDefault(r => r.Hardware == SensorHardware.Gpu && r.Kind == SensorKind.Temperature && r.Name == "GPU Core")?.Value;

    public float? CpuClock
    {
        get
        {
            var clocks = Readings.Where(r => r.Hardware == SensorHardware.Cpu && r.Kind == SensorKind.Clock && r.Name.StartsWith("Core #", StringComparison.Ordinal) && !r.Name.Contains("Effective", StringComparison.Ordinal) && r.Value is > 0).Select(r => r.Value!.Value).ToList();
            return clocks.Count > 0 ? clocks.Average() : null;
        }
    }

    public float? GpuClock => Readings.FirstOrDefault(r => r.Hardware == SensorHardware.Gpu && r.Kind == SensorKind.Clock && r.Name == "GPU Core")?.Value;

    public float? GpuPower
    {
        get
        {
            var power = Readings.Where(r => r.Hardware == SensorHardware.Gpu && r.Kind == SensorKind.Power && r.Value is > 0).ToList();
            return (power.FirstOrDefault(r => HasAny(r.Name, "Package", "Total", "Board")) ?? (power.Count > 0 ? power[0] : null))?.Value;
        }
    }

    public float? MemoryUsedGb => Physical(SensorKind.Data, "Memory Used");

    public float? MemoryAvailableGb => Physical(SensorKind.Data, "Memory Available");

    public float? GpuLoad => Readings.FirstOrDefault(r => r.Hardware == SensorHardware.Gpu && r.Kind == SensorKind.Load && r.Name == "GPU Core")?.Value;

    public float? MemoryLoad => Physical(SensorKind.Load, "Memory");

    private float? Physical(SensorKind kind, string name) =>
        Readings.FirstOrDefault(r => r.Hardware == SensorHardware.Memory && r.Kind == kind && r.Name == name && r.Id.StartsWith(PhysicalMemoryPrefix, StringComparison.Ordinal))?.Value;

    private static bool HasAny(string name, params string[] parts) => parts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
}

public enum SensorServiceState
{
    Stopped,
    Starting,
    Running,
    Paused,
    Failed,
}

public enum CpuSensorProblem
{
    None,
    NotElevated,
    PawnIoMissing,
    ReadFailed,
}
