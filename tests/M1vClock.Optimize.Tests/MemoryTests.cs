using M1vClock.Core.Results;
using M1vClock.Core.Security;
using M1vClock.Optimize.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Optimize.Tests;

public sealed class MemorySnapshotTests
{
    private static MemoryListSnapshot Snapshot() => new(
        PageSize: 4096,
        TotalPages: 4_000_000,
        ZeroPages: 100_000,
        FreePages: 50_000,
        ModifiedPages: 20_000,
        ModifiedNoWritePages: 5_000,
        StandbyPagesByPriority: [10_000, 20_000, 30_000, 40_000, 50_000, 60_000, 70_000, 80_000]);

    [Fact]
    public void ThePageCountsBecomeBytesAndTheListsAddUpToTheTotal()
    {
        var snapshot = Snapshot();

        Assert.Equal(360_000, snapshot.StandbyPages);
        Assert.Equal(360_000L * 4096, snapshot.StandbyBytes);
        Assert.Equal(150_000L * 4096, snapshot.FreeBytes);
        Assert.Equal(25_000L * 4096, snapshot.ModifiedBytes);
        Assert.Equal(4_000_000L * 4096, snapshot.TotalBytes);
        Assert.Equal(snapshot.TotalBytes, snapshot.InUseBytes + snapshot.FreeBytes + snapshot.StandbyBytes + snapshot.ModifiedBytes);
        Assert.Equal(snapshot.FreeBytes + snapshot.StandbyBytes, snapshot.AvailableBytes);
    }

    [Fact]
    public void TheStandbyAtEachPriorityIsReadAndOutOfRangePrioritiesAreZero()
    {
        var snapshot = Snapshot();

        Assert.Equal(10_000L * 4096, snapshot.StandbyBytesAtPriority(0));
        Assert.Equal(80_000L * 4096, snapshot.StandbyBytesAtPriority(7));
        Assert.Equal(0, snapshot.StandbyBytesAtPriority(8));
        Assert.Equal(0, snapshot.StandbyBytesAtPriority(-1));
    }

    [Fact]
    public void ListsThatAddUpToMoreThanTheTotalNeverGiveNegativeUsedMemory()
    {
        var odd = Snapshot() with { TotalPages = 100 };

        Assert.Equal(0, odd.InUseBytes);
    }
}

public sealed class MemoryListServiceTests
{
    private sealed class FakeSource : IMemoryListSource
    {
        public Queue<ReadResult<MemoryListSnapshot>> Reads { get; } = new();

        public PurgeAttempt Attempt { get; set; } = new(PurgeStatus.Done, null);

        public int Purges { get; private set; }

        public ReadResult<MemoryListSnapshot> Read() => Reads.Dequeue();

        public PurgeAttempt PurgeStandby()
        {
            Purges++;
            return Attempt;
        }
    }

    private static MemoryListSnapshot WithStandby(long pages) => new(4096, 4_000_000, 1000, 1000, 0, 0, [pages, 0, 0, 0, 0, 0, 0, 0]);

    [Fact]
    public async Task APurgeReportsHowMuchStandbyMemoryWentAway()
    {
        var source = new FakeSource();
        source.Reads.Enqueue(ReadResult.Success(WithStandby(500_000)));
        source.Reads.Enqueue(ReadResult.Success(WithStandby(20_000)));

        var result = await new MemoryListService(source).PurgeStandbyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PurgeStatus.Done, result.Status);
        Assert.Equal(500_000L * 4096, result.StandbyBefore);
        Assert.Equal(20_000L * 4096, result.StandbyAfter);
        Assert.Equal(480_000L * 4096, result.Freed);
        Assert.Equal(1, source.Purges);
    }

    [Theory]
    [InlineData(PurgeStatus.NeedsAdministrator)]
    [InlineData(PurgeStatus.Failed)]
    public async Task APurgeThatDidNotHappenFreesNothingAndDoesNotReadAgain(PurgeStatus status)
    {
        var source = new FakeSource { Attempt = new PurgeAttempt(status, "código") };
        source.Reads.Enqueue(ReadResult.Success(WithStandby(500_000)));

        var result = await new MemoryListService(source).PurgeStandbyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, result.Status);
        Assert.Equal(0, result.Freed);
        Assert.Equal("código", result.Error);
        Assert.Empty(source.Reads);
    }

    [Fact]
    public async Task ASourceThatCannotBeReadStillLetsThePurgeRunWithUnknownSizes()
    {
        var source = new FakeSource();
        source.Reads.Enqueue(ReadResult.Failure<MemoryListSnapshot>("sem leitura"));
        source.Reads.Enqueue(ReadResult.Failure<MemoryListSnapshot>("sem leitura"));

        var result = await new MemoryListService(source).PurgeStandbyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PurgeStatus.Done, result.Status);
        Assert.Equal(0, result.Freed);
    }

    [Fact]
    public async Task ReadingJustPassesTheSnapshotThrough()
    {
        var source = new FakeSource();
        source.Reads.Enqueue(ReadResult.Success(WithStandby(123)));

        var result = await new MemoryListService(source).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(123, result.Value?.StandbyPages);
    }
}

public sealed class RealMachineMemoryTests
{
    private static readonly NtMemoryListSource Source = new(NullLogger<NtMemoryListSource>.Instance);

    [Fact]
    public void TheMemoryListsOfThisPcAreReadWithoutAdministratorAndAddUp()
    {
        var result = Source.Read();

        Assert.True(result.TryGet(out var snapshot), result.Error);
        Assert.Equal(Environment.SystemPageSize, snapshot.PageSize);
        Assert.InRange(snapshot.TotalBytes, 1L << 30, 4L << 40);
        Assert.Equal(8, snapshot.StandbyPagesByPriority.Count);
        Assert.All(snapshot.StandbyPagesByPriority, p => Assert.True(p >= 0));
        Assert.True(snapshot.FreeBytes >= 0 && snapshot.ModifiedBytes >= 0);
        Assert.True(snapshot.FreeBytes + snapshot.StandbyBytes + snapshot.ModifiedBytes <= snapshot.TotalBytes);
        Assert.Equal(snapshot.TotalBytes, snapshot.InUseBytes + snapshot.FreeBytes + snapshot.StandbyBytes + snapshot.ModifiedBytes);
    }

    [Fact]
    public async Task ElevatedAndOnRequestThePurgeReallyEmptiesTheStandbyList()
    {
        Assert.SkipUnless(ElevationInfo.Current().IsElevated, "Precisa de administrador.");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("M1VCLOCK_REAL_ACTIONS") == "1", "Muda o sistema de verdade: defina M1VCLOCK_REAL_ACTIONS=1.");
        var service = new MemoryListService(Source);
        var before = (await service.ReadAsync(TestContext.Current.CancellationToken)).Value!;

        var result = await service.PurgeStandbyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PurgeStatus.Done, result.Status);
        Assert.Equal(before.StandbyBytes > 0, result.StandbyBefore > 0);
        Assert.True(result.StandbyAfter < result.StandbyBefore || result.StandbyBefore < 100L * 1024 * 1024, $"antes {result.StandbyBefore}, depois {result.StandbyAfter}");
        Console.WriteLine($"PURGA REAL: antes {result.StandbyBefore / 1048576} MB, depois {result.StandbyAfter / 1048576} MB, liberado {result.Freed / 1048576} MB");
    }

    [Fact]
    public void ThePurgeNeverRunsForRealInTestsAndWithoutAdministratorItAsksForIt()
    {
        if (ElevationInfo.Current().IsElevated)
        {
            return;
        }

        var attempt = Source.PurgeStandby();

        Assert.Equal(PurgeStatus.NeedsAdministrator, attempt.Status);
    }
}
