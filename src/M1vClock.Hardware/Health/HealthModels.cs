using M1vClock.Hardware.Power;
using M1vClock.Hardware.Sensors;
using M1vClock.Hardware.Storage;

namespace M1vClock.Hardware.Health;

public enum HealthKind
{
    CpuSensors,
    Disks,
    Memory,
    SystemDrive,
    PowerPlan,
    Battery,
    Usb,
}

public enum HealthStatus
{
    Ok,
    Attention,
    Critical,
    Unknown,
}

public sealed record HealthCheck(HealthKind Kind, HealthStatus Status, double Value = 0);

public sealed record HealthReport(IReadOnlyList<HealthCheck> Checks, HealthStatus Overall)
{
    public Volume? SystemVolume { get; init; }

    public int Problems => Checks.Count(c => c.Status is HealthStatus.Attention or HealthStatus.Critical);
}

public sealed record HealthInputs(
    CpuSensorProblem? CpuSensors,
    IReadOnlyList<DiskVerdict>? Disks,
    double? MemoryUsedPercent,
    double? SystemDriveFreePercent,
    PowerPlan? Plan,
    PowerSource? PowerSource,
    IReadOnlyList<BatteryVerdict>? Batteries,
    int? UsbProblems);
