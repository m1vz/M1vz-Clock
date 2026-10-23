using M1vClock.Core.Results;

namespace M1vClock.Optimize.Memory;

public sealed record MemoryListSnapshot(
    long PageSize,
    long TotalPages,
    long ZeroPages,
    long FreePages,
    long ModifiedPages,
    long ModifiedNoWritePages,
    IReadOnlyList<long> StandbyPagesByPriority)
{
    public long StandbyPages => StandbyPagesByPriority.Sum();

    public long TotalBytes => TotalPages * PageSize;

    public long FreeBytes => (ZeroPages + FreePages) * PageSize;

    public long StandbyBytes => StandbyPages * PageSize;

    public long ModifiedBytes => (ModifiedPages + ModifiedNoWritePages) * PageSize;

    public long InUseBytes => Math.Max(0, TotalBytes - FreeBytes - StandbyBytes - ModifiedBytes);

    public long AvailableBytes => FreeBytes + StandbyBytes;

    public long StandbyBytesAtPriority(int priority) => priority is >= 0 and < 8 && priority < StandbyPagesByPriority.Count ? StandbyPagesByPriority[priority] * PageSize : 0;
}

public enum PurgeStatus
{
    Done,
    NeedsAdministrator,
    Failed,
}

public sealed record PurgeAttempt(PurgeStatus Status, string? Message);

public sealed record PurgeResult(PurgeStatus Status, long StandbyBefore, long StandbyAfter, string? Error)
{
    public long Freed => Math.Max(0, StandbyBefore - StandbyAfter);
}

public interface IMemoryListSource
{
    ReadResult<MemoryListSnapshot> Read();

    PurgeAttempt PurgeStandby();
}

public interface IMemoryListService
{
    Task<ReadResult<MemoryListSnapshot>> ReadAsync(CancellationToken cancellationToken);

    Task<PurgeResult> PurgeStandbyAsync(CancellationToken cancellationToken);
}
