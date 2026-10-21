using System.Management;
using System.Runtime.InteropServices;
using M1vClock.Core.Privacy;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Storage;

public sealed class StorageCollector(IWmiReader wmi, ILogger<StorageCollector> logger)
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";
    private const string WmiScope = @"root\wmi";
    private const double UnsupportedAbove = 1e12;

    private static readonly string[] DriveProperties = ["Index", "Model", "SerialNumber", "FirmwareRevision", "InterfaceType", "Size", "PNPDeviceID"];
    private static readonly string[] PhysicalProperties = ["DeviceId", "HealthStatus", "MediaType", "BusType"];

    private static readonly string[] CounterProperties =
    [
        "DeviceId", "Temperature", "TemperatureMax", "Wear", "PowerOnHours", "ReadErrorsTotal", "WriteErrorsTotal",
        "ReadErrorsUncorrected", "WriteErrorsUncorrected", "StartStopCycleCount",
    ];

    private static readonly string[] PredictProperties = ["InstanceName", "PredictFailure"];
    private static readonly string[] AssociationProperties = ["Antecedent", "Dependent"];
    private static readonly string[] VolumeProperties = ["DeviceID", "VolumeName", "FileSystem", "Size", "FreeSpace", "DriveType"];

    public DiskInventory ReadDisks()
    {
        var rows = wmi.Query(new WmiQuery("Win32_DiskDrive", DriveProperties));
        var drives = rows.Select(r => new DriveRow(r)).OrderBy(d => d.Index).ToList();
        var physical = ReadOptional("MSFT_PhysicalDisk", () => wmi.Query(new WmiQuery("MSFT_PhysicalDisk", PhysicalProperties, StorageScope)), out _) ?? [];
        var counterRows = ReadOptional("MSFT_StorageReliabilityCounter", () => wmi.Query(new WmiQuery("MSFT_StorageReliabilityCounter", CounterProperties, StorageScope)), out var counterAccess);
        var predictRows = ReadOptional("MSStorageDriver_FailurePredictStatus", () => wmi.Query(new WmiQuery("MSStorageDriver_FailurePredictStatus", PredictProperties, WmiScope)), out var predictionAccess);
        var letters = ReadLetters();

        var disks = drives.Select(drive =>
        {
            var info = physical.FirstOrDefault(p => p.Number("DeviceId", -1) == drive.Index);
            var counters = counterRows?.FirstOrDefault(c => c.Number("DeviceId", -1) == drive.Index);
            return BuildDisk(drive, info, counters, predictRows, letters.GetValueOrDefault(drive.Index) ?? []);
        }).ToList();
        return new DiskInventory(disks, counterAccess, predictionAccess);
    }

    public IReadOnlyList<Volume> ReadVolumes(IReadOnlyList<PhysicalDisk> disks)
    {
        ArgumentNullException.ThrowIfNull(disks);
        var volumes = new List<Volume>();
        foreach (var row in wmi.Query(new WmiQuery("Win32_LogicalDisk", VolumeProperties)))
        {
            var total = row.Size("Size");
            var letter = row.Text("DeviceID");
            if (letter is null || total == 0)
            {
                continue;
            }

            var disk = disks.FirstOrDefault(d => d.Letters.Contains(letter, StringComparer.OrdinalIgnoreCase));
            volumes.Add(new Volume(
                letter,
                row.Text("VolumeName") ?? string.Empty,
                KindOf(row.Number("DriveType"), disk),
                row.Text("FileSystem") ?? string.Empty,
                disk?.Model ?? string.Empty,
                total,
                row.Size("FreeSpace")));
        }

        return volumes.OrderBy(v => v.Letter, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static double? Counter(WmiRow row, string name)
    {
        ArgumentNullException.ThrowIfNull(row);
        var value = row.Real(name);
        return value is null or < 0 or > UnsupportedAbove ? null : value;
    }

    public static DriveMedia MediaOf(int code) => code switch
    {
        3 => DriveMedia.Hdd,
        4 => DriveMedia.Ssd,
        5 => DriveMedia.Scm,
        _ => DriveMedia.Unknown,
    };

    public static DriveBus? BusOf(int code) => code switch
    {
        17 => DriveBus.Nvme,
        11 => DriveBus.Sata,
        7 => DriveBus.Usb,
        10 => DriveBus.Sas,
        8 => DriveBus.Raid,
        0 => null,
        _ => DriveBus.Other,
    };

    public static WindowsHealth HealthOf(int code) => code switch
    {
        0 => WindowsHealth.Healthy,
        1 => WindowsHealth.Warning,
        2 => WindowsHealth.Unhealthy,
        _ => WindowsHealth.Unknown,
    };

    public static VolumeKind KindOf(int driveType, PhysicalDisk? disk) => driveType switch
    {
        2 => VolumeKind.Removable,
        3 when disk?.Bus == DriveBus.Usb => VolumeKind.External,
        3 => VolumeKind.Internal,
        4 => VolumeKind.Network,
        5 => VolumeKind.Optical,
        _ => VolumeKind.Other,
    };

    private static PhysicalDisk BuildDisk(DriveRow drive, WmiRow? info, WmiRow? counters, IReadOnlyList<WmiRow>? predictions, IReadOnlyList<string> letters)
    {
        var bus = info is null ? null : BusOf(info.Number("BusType"));
        var resolvedBus = bus ?? (drive.IsUsb ? DriveBus.Usb : DriveBus.Unknown);
        bool? predicted = null;
        if (predictions is not null && drive.Pnp.Length > 0)
        {
            var matching = predictions.Where(p => p.Text("InstanceName")?.StartsWith(drive.Pnp, StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (matching.Count > 0)
            {
                predicted = matching.Any(p => p.Bool("PredictFailure") == true);
            }
        }

        return new PhysicalDisk(
            drive.Index,
            drive.Model,
            new SensitiveText(PlatformCollector.Clean(drive.Serial)),
            drive.Firmware,
            resolvedBus,
            info is null ? DriveMedia.Unknown : MediaOf(info.Number("MediaType")),
            drive.Size,
            info is null ? WindowsHealth.Unknown : HealthOf(info.Number("HealthStatus", 5)),
            predicted,
            counters is null ? null : ToCounters(counters),
            letters);
    }

    private static StorageCounters ToCounters(WmiRow row) => new(
        Counter(row, "Temperature"),
        Counter(row, "TemperatureMax"),
        Counter(row, "Wear"),
        (long?)Counter(row, "PowerOnHours"),
        (long?)Counter(row, "ReadErrorsTotal"),
        (long?)Counter(row, "WriteErrorsTotal"),
        (long?)Counter(row, "ReadErrorsUncorrected"),
        (long?)Counter(row, "WriteErrorsUncorrected"),
        (long?)Counter(row, "StartStopCycleCount"));

    private Dictionary<int, List<string>> ReadLetters()
    {
        var result = new Dictionary<int, List<string>>();
        var associations = ReadOptional("Win32_DiskDriveToDiskPartition", () => wmi.Query(new WmiQuery("Win32_DiskDriveToDiskPartition", AssociationProperties)), out _);
        var logical = ReadOptional("Win32_LogicalDiskToPartition", () => wmi.Query(new WmiQuery("Win32_LogicalDiskToPartition", AssociationProperties)), out _);
        if (associations is null || logical is null)
        {
            return result;
        }

        var diskOfPartition = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in associations)
        {
            if (WmiReference.DeviceId(row.Text("Antecedent")) is { } disk && WmiReference.DeviceId(row.Text("Dependent")) is { } partition)
            {
                diskOfPartition[partition] = disk;
            }
        }

        foreach (var row in logical)
        {
            if (WmiReference.DeviceId(row.Text("Antecedent")) is not { } partition
                || WmiReference.DeviceId(row.Text("Dependent")) is not { } letter
                || !diskOfPartition.TryGetValue(partition, out var disk)
                || DriveIndex(disk) is not { } index)
            {
                continue;
            }

            if (!result.TryGetValue(index, out var list))
            {
                result[index] = list = [];
            }

            if (!list.Contains(letter, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(letter);
            }
        }

        foreach (var list in result.Values)
        {
            list.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    private static int? DriveIndex(string deviceId)
    {
        const string Marker = "PHYSICALDRIVE";
        var at = deviceId.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        return at >= 0 && int.TryParse(deviceId[(at + Marker.Length)..], out var index) ? index : null;
    }

    private IReadOnlyList<WmiRow>? ReadOptional(string source, Func<IReadOnlyList<WmiRow>> read, out CounterAccess access)
    {
        try
        {
            var rows = read();
            access = CounterAccess.Read;
            return rows;
        }
        catch (UnauthorizedAccessException)
        {
            HardwareLog.OptionalSourceDenied(logger, source);
            access = CounterAccess.NeedsAdministrator;
            return null;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or InvalidOperationException or TimeoutException)
        {
            HardwareLog.OptionalSourceFailed(logger, ex, source);
            access = CounterAccess.Unavailable;
            return null;
        }
    }

    private sealed class DriveRow(WmiRow row)
    {
        public int Index { get; } = row.Number("Index");

        public string Model { get; } = row.Text("Model") ?? string.Empty;

        public string? Serial { get; } = row.Text("SerialNumber");

        public string Firmware { get; } = row.Text("FirmwareRevision") ?? string.Empty;

        public string Pnp { get; } = row.Text("PNPDeviceID") ?? string.Empty;

        public ulong Size { get; } = row.Size("Size");

        public bool IsUsb => Pnp.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase);
    }
}
