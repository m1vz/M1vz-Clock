using M1vClock.Core.Privacy;
using M1vClock.Core.Results;

namespace M1vClock.Hardware.Storage;

public enum DriveMedia
{
    Unknown,
    Hdd,
    Ssd,
    Scm,
}

public enum DriveBus
{
    Unknown,
    Sata,
    Nvme,
    Usb,
    Sas,
    Raid,
    Other,
}

public enum WindowsHealth
{
    Unknown,
    Healthy,
    Warning,
    Unhealthy,
}

public enum CounterAccess
{
    Read,
    NeedsAdministrator,
    Unavailable,
}

public sealed record StorageCounters(
    double? Temperature,
    double? TemperatureMax,
    double? Wear,
    long? PowerOnHours,
    long? ReadErrors,
    long? WriteErrors,
    long? ReadErrorsUncorrected,
    long? WriteErrorsUncorrected,
    long? StartStopCycles)
{
    public ulong? BytesWritten { get; init; }

    public ulong? BytesRead { get; init; }
}

public sealed record PhysicalDisk(
    int Index,
    string Model,
    SensitiveText Serial,
    string Firmware,
    DriveBus Bus,
    DriveMedia Media,
    ulong SizeBytes,
    WindowsHealth Health,
    bool? FailurePredicted,
    StorageCounters? Counters,
    IReadOnlyList<string> Letters)
{
    public bool IsSolidState => Media == DriveMedia.Ssd || Bus == DriveBus.Nvme;
}

public sealed record DiskInventory(IReadOnlyList<PhysicalDisk> Disks, CounterAccess CounterAccess, CounterAccess PredictionAccess);

public enum VolumeKind
{
    Internal,
    External,
    Removable,
    Network,
    Optical,
    Other,
}

public sealed record Volume(string Letter, string Label, VolumeKind Kind, string FileSystem, string DiskModel, ulong TotalBytes, ulong FreeBytes)
{
    public ulong UsedBytes => TotalBytes > FreeBytes ? TotalBytes - FreeBytes : 0;

    public double UsedFraction => TotalBytes == 0 ? 0 : (double)UsedBytes / TotalBytes;
}

public enum DiskVerdict
{
    Good,
    Warning,
    Critical,
    Unknown,
}

public enum FindingKind
{
    FailurePredicted,
    WindowsUnhealthy,
    WindowsWarning,
    WearEndOfLife,
    WearHigh,
    TemperatureCritical,
    TemperatureHigh,
    UncorrectedErrors,
}

public enum FindingSeverity
{
    Warning,
    Critical,
}

public sealed record DiskFinding(FindingKind Kind, FindingSeverity Severity, double Value = 0, long Second = 0);

public sealed record DiskAssessment(DiskVerdict Verdict, IReadOnlyList<DiskFinding> Findings);

public sealed record DiskReport(PhysicalDisk Disk, DiskAssessment Assessment);

public sealed record StorageReport(ReadResult<IReadOnlyList<DiskReport>> Disks, ReadResult<IReadOnlyList<Volume>> Volumes, CounterAccess CounterAccess, CounterAccess PredictionAccess);
