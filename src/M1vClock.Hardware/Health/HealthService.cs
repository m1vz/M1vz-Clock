using M1vClock.Hardware.Devices;
using M1vClock.Hardware.Power;
using M1vClock.Hardware.Sensors;
using M1vClock.Hardware.Storage;

namespace M1vClock.Hardware.Health;

public interface IHealthService
{
    Task<HealthReport> ReadAsync(SensorSnapshot? snapshot, bool elevated, CancellationToken cancellationToken);
}

public sealed class HealthService : IHealthService
{
    private readonly IStorageService _storage;
    private readonly IPowerService _power;
    private readonly IUsbDeviceService _usb;
    private readonly IPawnIoProbe _pawnIo;
    private readonly string _systemDrive;

    public HealthService(IStorageService storage, IPowerService power, IUsbDeviceService usb, IPawnIoProbe pawnIo, string? systemDrive = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(usb);
        ArgumentNullException.ThrowIfNull(pawnIo);
        _storage = storage;
        _power = power;
        _usb = usb;
        _pawnIo = pawnIo;
        _systemDrive = NormalizeDrive(systemDrive ?? Path.GetPathRoot(Environment.SystemDirectory) ?? "C:");
    }

    public async Task<HealthReport> ReadAsync(SensorSnapshot? snapshot, bool elevated, CancellationToken cancellationToken)
    {
        var storage = _storage.ReadAsync(cancellationToken);
        var power = _power.ReadAsync(cancellationToken);
        var usb = _usb.ReadAsync(cancellationToken);
        await Task.WhenAll(storage, power, usb).ConfigureAwait(false);
        var storageReport = await storage.ConfigureAwait(false);
        var powerReport = await power.ConfigureAwait(false);
        var usbResult = await usb.ConfigureAwait(false);

        var inputs = new HealthInputs(
            snapshot is null ? null : CpuSensorDiagnostics.Diagnose(snapshot, elevated, _pawnIo.IsInstalled),
            storageReport.Disks.TryGet(out var disks) ? [.. disks.Select(d => d.Assessment.Verdict)] : null,
            snapshot?.MemoryLoad,
            SystemDriveFreePercent(storageReport),
            powerReport.Plan.TryGet(out var plan) ? plan : null,
            powerReport.Source,
            powerReport.Batteries.TryGet(out var batteries) ? [.. batteries.Select(b => b.Assessment.Verdict)] : null,
            usbResult.TryGet(out var devices) ? devices.Count(d => d.HasProblem) : null);
        return HealthEvaluator.Evaluate(inputs) with { SystemVolume = SystemVolume(storageReport) };
    }

    private static string NormalizeDrive(string drive) => drive.TrimEnd('\\', '/').ToUpperInvariant();

    private Volume? SystemVolume(StorageReport report) =>
        report.Volumes.TryGet(out var volumes)
            ? volumes.FirstOrDefault(v => string.Equals(NormalizeDrive(v.Letter), _systemDrive, StringComparison.Ordinal))
            : null;

    private double? SystemDriveFreePercent(StorageReport report) =>
        SystemVolume(report) is { TotalBytes: > 0 } volume ? 100.0 * volume.FreeBytes / volume.TotalBytes : null;
}
