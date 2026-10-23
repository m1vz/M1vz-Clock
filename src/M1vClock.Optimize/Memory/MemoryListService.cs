namespace M1vClock.Optimize.Memory;

public sealed class MemoryListService(IMemoryListSource source) : IMemoryListService
{
    public Task<M1vClock.Core.Results.ReadResult<MemoryListSnapshot>> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(source.Read, cancellationToken);

    public Task<PurgeResult> PurgeStandbyAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var before = source.Read();
                var beforeBytes = before.TryGet(out var first) ? first.StandbyBytes : 0;
                var attempt = source.PurgeStandby();
                if (attempt.Status != PurgeStatus.Done)
                {
                    return new PurgeResult(attempt.Status, beforeBytes, beforeBytes, attempt.Message);
                }

                var after = source.Read();
                return new PurgeResult(PurgeStatus.Done, beforeBytes, after.TryGet(out var second) ? second.StandbyBytes : 0, null);
            },
            cancellationToken);
}
