namespace M1vClock.Hardware.Devices;

public sealed record DeviceBatch(DeviceDiff Diff, bool VolumeJustNow, DateTimeOffset At);

public static class DeviceChangeDetector
{
    public static DeviceDiff Diff(IReadOnlyCollection<UsbDeviceInfo> before, IReadOnlyCollection<UsbDeviceInfo> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var previous = before.Select(d => d.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = after.Select(d => d.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new DeviceDiff(
            [.. after.Where(d => !previous.Contains(d.InstanceId))],
            [.. before.Where(d => !current.Contains(d.InstanceId))]);
    }

    public static UsbDeviceInfo? MostDescriptive(IReadOnlyList<UsbDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return devices.FirstOrDefault(d => d.ComPort is not null)
            ?? devices.FirstOrDefault(d => d.Maker is not null)
            ?? devices.FirstOrDefault(d => d.Category != UsbCategory.Hub)
            ?? (devices.Count > 0 ? devices[0] : null);
    }

    public static IReadOnlyList<DeviceChange> Announcements(DeviceBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.VolumeJustNow)
        {
            return [];
        }

        var changes = new List<DeviceChange>();
        if (MostDescriptive([.. batch.Diff.Added.Where(d => d.Category != UsbCategory.Storage)]) is { } added)
        {
            changes.Add(new DeviceChange(DeviceChangeKind.Connected, added, batch.At));
        }

        if (MostDescriptive([.. batch.Diff.Removed.Where(d => d.Category != UsbCategory.Storage)]) is { } removed)
        {
            changes.Add(new DeviceChange(DeviceChangeKind.Disconnected, removed, batch.At));
        }

        return changes;
    }
}
