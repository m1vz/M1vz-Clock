using M1vClock.Hardware.Sensors;

namespace M1vClock.Hardware.Storage;

public static class StorageSensorMerge
{
    private const double BytesPerGigabyte = 1e9;

    public static IReadOnlyList<PhysicalDisk> Apply(IReadOnlyList<PhysicalDisk> disks, SensorSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(disks);
        if (snapshot is null)
        {
            return disks;
        }

        var storage = snapshot.Readings.Where(r => r.Hardware == SensorHardware.Storage).ToList();
        return [.. disks.Select(d => Merge(d, storage, disks))];
    }

    private static PhysicalDisk Merge(PhysicalDisk disk, List<SensorReading> storage, IReadOnlyList<PhysicalDisk> all)
    {
        var mine = storage.Where(r => string.Equals(r.HardwareName, disk.Model, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mine.Count == 0 || all.Count(d => string.Equals(d.Model, disk.Model, StringComparison.OrdinalIgnoreCase)) != 1 || DistinctDevices(mine) != 1)
        {
            return disk;
        }

        var counters = disk.Counters ?? new StorageCounters(null, null, null, null, null, null, null, null, null);
        var merged = counters with
        {
            Temperature = counters.Temperature ?? Temperature(mine),
            Wear = MaxOf(counters.Wear, Value(mine, "Percentage Used")),
            PowerOnHours = counters.PowerOnHours ?? Whole(Value(mine, "Power On Hours")),
            StartStopCycles = counters.StartStopCycles ?? Whole(Value(mine, "Power On Count")),
            BytesWritten = counters.BytesWritten ?? Bytes(Value(mine, "Data Written")),
            BytesRead = counters.BytesRead ?? Bytes(Value(mine, "Data Read")),
        };
        return merged == counters ? disk : disk with { Counters = merged };
    }

    private static int DistinctDevices(List<SensorReading> readings) =>
        readings.Select(r => string.Join('/', r.Id.Split('/', StringSplitOptions.RemoveEmptyEntries).Take(2))).Distinct(StringComparer.Ordinal).Count();

    private static double? Temperature(List<SensorReading> readings)
    {
        var temperatures = readings.Where(r => r.Kind == SensorKind.Temperature && r.Value is > 0).ToList();
        var main = temperatures.FirstOrDefault(r => r.Name is "Temperature" or "Composite Temperature") ?? (temperatures.Count > 0 ? temperatures[0] : null);
        return main?.Value;
    }

    private static double? Value(List<SensorReading> readings, string name) =>
        readings.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) && r.Value is not null)?.Value;

    private static double? MaxOf(double? first, double? second) => first is null ? second : second is null ? first : Math.Max(first.Value, second.Value);

    private static long? Whole(double? value) => value is { } number and >= 0 ? (long)Math.Round(number) : null;

    private static ulong? Bytes(double? gigabytes) => gigabytes is { } value and > 0 ? (ulong)(value * BytesPerGigabyte) : null;
}
