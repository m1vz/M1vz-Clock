using M1vClock.Hardware.Power;
using M1vClock.Hardware.Sensors;
using M1vClock.Hardware.Storage;

namespace M1vClock.Hardware.Health;

public static class HealthEvaluator
{
    public const double MemoryAttentionPercent = 85;
    public const double MemoryCriticalPercent = 95;
    public const double DriveAttentionFreePercent = 10;
    public const double DriveCriticalFreePercent = 5;

    public static HealthReport Evaluate(HealthInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var checks = new List<HealthCheck>();
        AddCpuSensors(inputs, checks);
        AddDisks(inputs, checks);
        AddMemory(inputs, checks);
        AddSystemDrive(inputs, checks);
        AddPowerPlan(inputs, checks);
        AddBattery(inputs, checks);
        AddUsb(inputs, checks);
        return new HealthReport(checks, Overall(checks));
    }

    public static HealthStatus Overall(IReadOnlyList<HealthCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        if (checks.Any(c => c.Status == HealthStatus.Critical))
        {
            return HealthStatus.Critical;
        }

        if (checks.Any(c => c.Status == HealthStatus.Attention))
        {
            return HealthStatus.Attention;
        }

        return checks.Any(c => c.Status == HealthStatus.Ok) ? HealthStatus.Ok : HealthStatus.Unknown;
    }

    private static void AddCpuSensors(HealthInputs inputs, List<HealthCheck> checks)
    {
        if (inputs.CpuSensors is { } problem)
        {
            checks.Add(new HealthCheck(HealthKind.CpuSensors, problem == CpuSensorProblem.None ? HealthStatus.Ok : HealthStatus.Attention, (int)problem));
        }
    }

    private static void AddDisks(HealthInputs inputs, List<HealthCheck> checks)
    {
        if (inputs.Disks is not { Count: > 0 } verdicts)
        {
            return;
        }

        var problems = verdicts.Count(v => v is DiskVerdict.Warning or DiskVerdict.Critical);
        var status = verdicts.Contains(DiskVerdict.Critical) ? HealthStatus.Critical
            : verdicts.Contains(DiskVerdict.Warning) ? HealthStatus.Attention
            : verdicts.All(v => v == DiskVerdict.Unknown) ? HealthStatus.Unknown
            : HealthStatus.Ok;
        checks.Add(new HealthCheck(HealthKind.Disks, status, problems));
    }

    private static void AddMemory(HealthInputs inputs, List<HealthCheck> checks)
    {
        if (inputs.MemoryUsedPercent is not { } used)
        {
            return;
        }

        var status = used >= MemoryCriticalPercent ? HealthStatus.Critical : used >= MemoryAttentionPercent ? HealthStatus.Attention : HealthStatus.Ok;
        checks.Add(new HealthCheck(HealthKind.Memory, status, used));
    }

    private static void AddSystemDrive(HealthInputs inputs, List<HealthCheck> checks)
    {
        if (inputs.SystemDriveFreePercent is not { } free)
        {
            return;
        }

        var status = free < DriveCriticalFreePercent ? HealthStatus.Critical : free < DriveAttentionFreePercent ? HealthStatus.Attention : HealthStatus.Ok;
        checks.Add(new HealthCheck(HealthKind.SystemDrive, status, free));
    }

    private static void AddPowerPlan(HealthInputs inputs, List<HealthCheck> checks)
    {
        if (inputs.Plan is null || inputs.PowerSource is null or PowerSource.Battery)
        {
            return;
        }

        checks.Add(new HealthCheck(HealthKind.PowerPlan, inputs.Plan.IsKnownAndSlow ? HealthStatus.Attention : HealthStatus.Ok));
    }

    private static void AddBattery(HealthInputs inputs, List<HealthCheck> checks)
    {
        if (inputs.Batteries is not { Count: > 0 } verdicts)
        {
            return;
        }

        var status = verdicts.Contains(BatteryVerdict.Worn) ? HealthStatus.Critical
            : verdicts.Contains(BatteryVerdict.Warning) ? HealthStatus.Attention
            : verdicts.All(v => v == BatteryVerdict.Unknown) ? HealthStatus.Unknown
            : HealthStatus.Ok;
        checks.Add(new HealthCheck(HealthKind.Battery, status));
    }

    private static void AddUsb(HealthInputs inputs, List<HealthCheck> checks)
    {
        if (inputs.UsbProblems is { } problems)
        {
            checks.Add(new HealthCheck(HealthKind.Usb, problems > 0 ? HealthStatus.Attention : HealthStatus.Ok, problems));
        }
    }
}
