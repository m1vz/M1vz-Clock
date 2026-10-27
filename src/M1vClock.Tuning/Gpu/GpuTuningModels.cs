namespace M1vClock.Tuning.Gpu;

public enum GpuVendor
{
    Amd,
    Nvidia,
}

public enum PowerUnit
{
    Percent,
    Watts,
}

public readonly record struct TuningRange(int Minimum, int Maximum, int Step)
{
    public bool Contains(int value) => value >= Minimum && value <= Maximum;
}

public sealed record GpuTuningInfo(
    GpuVendor Vendor,
    string Name,
    bool ManualClockSupported,
    bool ManualPowerSupported,
    bool? AtFactory,
    TuningRange? ClockRange,
    int? ClockCurrent,
    TuningRange? VoltageRange,
    int? VoltageCurrent,
    TuningRange? PowerRange,
    int? PowerCurrent,
    int? PowerDefault,
    PowerUnit PowerUnit);

public enum GpuReadStatus
{
    Read,
    LibraryMissing,
    NoDevice,
    Failed,
}

public sealed record GpuTuningReading(GpuReadStatus Status, IReadOnlyList<GpuTuningInfo> Gpus, string? Error = null)
{
    public static GpuTuningReading Missing { get; } = new(GpuReadStatus.LibraryMissing, []);
}

public interface IGpuTuningReader
{
    GpuVendor Vendor { get; }

    GpuTuningReading Read();
}
