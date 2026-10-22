using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Storage;

public interface IStorageService
{
    Task<StorageReport> ReadAsync(CancellationToken cancellationToken);
}

public sealed class StorageService(StorageCollector collector, ISensorService sensors, TimeProvider clock, ILogger<StorageService> logger) : IStorageService
{
    public static readonly TimeSpan MaxSnapshotAge = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public async Task<StorageReport> ReadAsync(CancellationToken cancellationToken)
    {
        var snapshot = await FreshSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var inventory = await SectionReader.ReadAsync(logger, "discos", collector.ReadDisks, cancellationToken).ConfigureAwait(false);
        inventory = inventory.Map(i => i with { Disks = StorageSensorMerge.Apply(i.Disks, snapshot) });
        var known = inventory.TryGet(out var value) ? value.Disks : [];
        var volumes = await SectionReader.ReadAsync(logger, "unidades", () => collector.ReadVolumes(known), cancellationToken).ConfigureAwait(false);
        var disks = inventory.Map(i => (IReadOnlyList<DiskReport>)i.Disks.Select(d => new DiskReport(d, DiskHealthEvaluator.Evaluate(d))).ToList());
        var access = inventory.TryGet(out var read) ? read : null;
        return new StorageReport(
            disks,
            volumes.Map(v => (IReadOnlyList<Volume>)v),
            access?.CounterAccess ?? CounterAccess.Unavailable,
            access?.PredictionAccess ?? CounterAccess.Unavailable);
    }

    private async Task<SensorSnapshot?> FreshSnapshotAsync(CancellationToken cancellationToken)
    {
        using var lease = sensors.Subscribe();
        var deadline = clock.GetUtcNow() + MaxWait;
        while (true)
        {
            var snapshot = sensors.Latest;
            var state = sensors.State;
            if (snapshot is not null && (state == SensorServiceState.Paused || clock.GetUtcNow() - snapshot.At <= MaxSnapshotAge))
            {
                return snapshot;
            }

            if (state == SensorServiceState.Failed || clock.GetUtcNow() >= deadline)
            {
                return null;
            }

            await Task.Delay(PollInterval, clock, cancellationToken).ConfigureAwait(false);
        }
    }
}
