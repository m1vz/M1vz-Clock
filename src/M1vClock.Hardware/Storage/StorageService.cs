using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Storage;

public interface IStorageService
{
    Task<StorageReport> ReadAsync(CancellationToken cancellationToken);
}

public sealed class StorageService(StorageCollector collector, ILogger<StorageService> logger) : IStorageService
{
    public async Task<StorageReport> ReadAsync(CancellationToken cancellationToken)
    {
        var inventory = await SectionReader.ReadAsync(logger, "discos", collector.ReadDisks, cancellationToken).ConfigureAwait(false);
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
}
