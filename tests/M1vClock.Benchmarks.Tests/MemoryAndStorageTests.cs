using M1vClock.Benchmarks.Disk;
using M1vClock.Benchmarks.Memory;

namespace M1vClock.Benchmarks.Tests;

public sealed class CollectedProgress : IProgress<BenchmarkProgress>
{
    private readonly Lock _gate = new();
    private readonly List<BenchmarkProgress> _items = [];

    public IReadOnlyList<BenchmarkProgress> Items
    {
        get
        {
            lock (_gate)
            {
                return [.. _items];
            }
        }
    }

    public void Report(BenchmarkProgress value)
    {
        lock (_gate)
        {
            _items.Add(value);
        }
    }
}

public sealed class MemoryBenchmarkTests
{
    private static MemoryBenchmark Small(Func<long>? available = null, int steps = 200_000) =>
        new(latencyBytes: 4L << 20, bandwidthBytes: 8L << 20, latencySteps: steps, availableBytes: available ?? (() => long.MaxValue));

    [Fact]
    public void TheDefaultsAreTheSizesTheOriginalUsed()
    {
        Assert.Equal(256L << 20, MemoryBenchmark.DefaultLatencyBytes);
        Assert.Equal(1L << 30, MemoryBenchmark.DefaultBandwidthBytes);
        Assert.Equal(20_000_000, MemoryBenchmark.DefaultLatencySteps);
    }

    [Fact]
    public void ALatencyRunGivesAPlausibleNumberOfNanoseconds()
    {
        var nanoseconds = Small().MeasureLatencyNanoseconds(TestContext.Current.CancellationToken);

        Assert.InRange(nanoseconds, 0.1, 5000);
    }

    [Fact]
    public void BandwidthIsPositiveAndCopyCountsReadingAndWriting()
    {
        var (read, copy) = Small().MeasureBandwidth(TestContext.Current.CancellationToken);

        Assert.True(read > 0.1);
        Assert.True(copy > 0.1);
    }

    [Fact]
    public void ARunReportsItsPhasesInOrderAndEndsAtOneHundredPercent()
    {
        var progress = new CollectedProgress();

        var result = Small().Run(progress, TestContext.Current.CancellationToken);

        Assert.True(result.LatencyNanoseconds > 0);
        Assert.Equal(["Bench.Phase.MemoryLatency", "Bench.Phase.MemoryBandwidth", "Bench.Phase.Done"], progress.Items.Select(i => i.Phase));
        Assert.Equal(1, progress.Items[^1].Fraction);
    }

    [Fact]
    public void NotEnoughFreeMemoryStopsTheTestBeforeAllocatingAnything()
    {
        var benchmark = Small(() => 100L << 20);

        Assert.Throws<InsufficientMemoryException>(() => benchmark.MeasureLatencyNanoseconds(TestContext.Current.CancellationToken));
        Assert.Throws<InsufficientMemoryException>(() => benchmark.MeasureBandwidth(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TheRequirementIncludesASafetyMargin()
    {
        var benchmark = Small();

        Assert.Equal((4L << 20) + MemoryBenchmark.SafetyMarginBytes, benchmark.LatencyRequiredBytes);
        Assert.Equal(2 * (8L << 20) + MemoryBenchmark.SafetyMarginBytes, benchmark.BandwidthRequiredBytes);
    }

    [Fact]
    public void CancellingTheLatencyLoopStopsItEarly()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Small(steps: 50_000_000).MeasureLatencyNanoseconds(cancellation.Token));
    }

    [Fact]
    public void CancellingBeforeTheBandwidthRepetitionsStopsIt()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Small().MeasureBandwidth(cancellation.Token));
    }

    [Theory]
    [InlineData(0, 8, 1000)]
    [InlineData(4, 0, 1000)]
    public void SizesBelowOneMegabyteAreRejected(long latencyMegabytes, long bandwidthMegabytes, int steps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryBenchmark(latencyMegabytes << 20, bandwidthMegabytes << 20, steps));
    }

    [Fact]
    public void TheRealFreeMemoryOfThisPcIsReported()
    {
        Assert.True(M1vClock.Benchmarks.Io.PhysicalMemory.AvailableBytes() > 64L << 20);
    }
}

public sealed class LoadingBenchmarkTests : IDisposable
{
    private const long SmallPack = 8L << 20;

    private readonly ScratchDirectory _work = new();
    private readonly CollectingLogger<LoadingBenchmark> _logger = new();

    public void Dispose() => _work.Dispose();

    private LoadingBenchmark Create(long pack = SmallPack, int assets = 24) => new(_logger, pack, assets);

    [Fact]
    public void ARunMeasuresThePackAndTheSmallFilesAndLeavesNothingBehind()
    {
        var result = Create().Run(_work.Path, hardened: false, progress: null, TestContext.Current.CancellationToken);

        Assert.True(result.SequentialReadMegabytesPerSecond > 1);
        Assert.True(result.SmallFilesMegabytesPerSecond > 0);
        Assert.True(result.FilesPerSecond > 0);
        Assert.True(result.MapLoadSeconds > 0);
        Assert.Equal(Path.GetPathRoot(_work.Path)!.TrimEnd('\\'), result.Drive);
        Assert.Empty(_work.BenchFolders());
    }

    [Fact]
    public void TheMapLoadTimeIsThePackTimePlusTheSmallFilesTime()
    {
        var result = Create().Run(_work.Path, hardened: false, progress: null, TestContext.Current.CancellationToken);

        var packSeconds = SmallPack / 1048576.0 / result.SequentialReadMegabytesPerSecond;
        var assetSeconds = 24 / result.FilesPerSecond;
        Assert.Equal(packSeconds + assetSeconds, result.MapLoadSeconds, 3);
    }

    [Fact]
    public void ProgressNeverGoesBackwardsAndEndsComplete()
    {
        var progress = new CollectedProgress();

        Create().Run(_work.Path, hardened: false, progress, TestContext.Current.CancellationToken);

        var phases = progress.Items.Select(i => i.Phase).Distinct().ToList();
        Assert.Equal(["Bench.Phase.LoadPrepare", "Bench.Phase.LoadPack", "Bench.Phase.LoadAntivirus", "Bench.Phase.LoadAssets", "Bench.Phase.Done"], phases);
        Assert.Equal(1, progress.Items[^1].Fraction);
        Assert.All(progress.Items, i => Assert.InRange(i.Fraction, 0, 1));
    }

    [Fact]
    public void CancellingMidwayRemovesEverythingTheTestWrote()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(p =>
        {
            if (p.Phase == "Bench.Phase.LoadPack")
            {
                cancellation.Cancel();
            }
        });
        var benchmark = Create(32L << 20, 24);

        Assert.ThrowsAny<OperationCanceledException>(() => benchmark.Run(_work.Path, hardened: false, progress, cancellation.Token));

        Assert.Empty(_work.BenchFolders());
    }

    [Fact]
    public void NotEnoughFreeSpaceIsReportedBeforeWritingAnything()
    {
        var benchmark = new LoadingBenchmark(_logger, packBytes: 1L << 50, assetCount: 24);

        var error = Assert.Throws<IOException>(() => benchmark.Run(_work.Path, hardened: false, progress: null, TestContext.Current.CancellationToken));

        Assert.Contains("espaço", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_work.BenchFolders());
    }

    [Theory]
    [InlineData(1L << 20, 24)]
    [InlineData(6L << 20, 24)]
    [InlineData(8L << 20, 4)]
    public void APackNotMadeOfWholeChunksOrTooFewFilesIsRejected(long pack, int assets)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoadingBenchmark(_logger, pack, assets));
    }

    [Fact]
    public void TheRequiredSpaceCoversThePackTheFilesAndTheMargin()
    {
        Assert.Equal(SmallPack + 24L * LoadingBenchmark.AssetBytes + (512L << 20), Create().RequiredBytes);
    }
}

public sealed class InlineProgress(Action<BenchmarkProgress> report) : IProgress<BenchmarkProgress>
{
    public void Report(BenchmarkProgress value) => report(value);
}

public sealed class DiskBenchmarkTests : IDisposable
{
    private static readonly TimeSpan ShortRandom = TimeSpan.FromMilliseconds(150);

    private readonly ScratchDirectory _work = new();
    private readonly CollectingLogger<DiskBenchmark> _logger = new();

    public void Dispose() => _work.Dispose();

    private DiskBenchmark Create() => new(_logger, ShortRandom);

    [Fact]
    public async Task ARunFillsEveryMetricAndLeavesNothingBehind()
    {
        var result = await Create().RunAsync(_work.Path, false, 16, null, TestContext.Current.CancellationToken);

        Assert.True(result.SequentialReadQ1 > 1);
        Assert.True(result.SequentialWriteQ1 > 1);
        Assert.True(result.SequentialReadQ8 > 1);
        Assert.True(result.SequentialWriteQ8 > 1);
        Assert.True(result.RandomReadIopsQ1 > 10);
        Assert.True(result.RandomWriteIopsQ1 > 10);
        Assert.True(result.RandomReadIopsQ8 > 10);
        Assert.True(result.RandomReadIopsQ32 > 10);
        Assert.True(result.RandomWriteIopsQ32 > 10);
        Assert.True(result.ReadLatencyMicroseconds > 0);
        Assert.True(result.WriteLatencyMicroseconds > 0);
        Assert.True(result.ReadLatencyP99Microseconds >= result.ReadLatencyMicroseconds * 0.5);
        Assert.Equal(16, result.FileMegabytes);
        Assert.Equal(Path.GetPathRoot(_work.Path)!.TrimEnd('\\'), result.Drive);
        Assert.True(result.Score > 0);
        Assert.Empty(_work.BenchFolders());
    }

    [Fact]
    public async Task ProgressWalksThroughEveryPhaseOnceAndEndsComplete()
    {
        var progress = new CollectedProgress();

        await Create().RunAsync(_work.Path, false, 16, progress, TestContext.Current.CancellationToken);

        var phases = progress.Items.Select(i => i.Phase).ToList();
        Assert.Equal(
            [
                "Bench.Phase.DiskSeqWrite", "Bench.Phase.DiskSeqRead", "Bench.Phase.DiskSeqWriteQ8", "Bench.Phase.DiskSeqReadQ8",
                "Bench.Phase.DiskRandReadQ1", "Bench.Phase.DiskRandWriteQ1", "Bench.Phase.DiskRandReadQ8",
                "Bench.Phase.DiskRandReadQ32", "Bench.Phase.DiskRandWriteQ32", "Bench.Phase.Done",
            ],
            phases);
        Assert.Equal(progress.Items.Select(i => i.Fraction).Order(), progress.Items.Select(i => i.Fraction));
        Assert.Equal(1, progress.Items[^1].Fraction);
    }

    [Fact]
    public async Task CancellingInTheMiddleStopsAndRemovesTheTestFile()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(p =>
        {
            if (p.Phase == "Bench.Phase.DiskRandReadQ1")
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().RunAsync(_work.Path, false, 16, progress, cancellation.Token));

        Assert.Empty(_work.BenchFolders());
    }

    [Fact]
    public async Task AFileSmallerThanTheMinimumIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Create().RunAsync(_work.Path, false, 4, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NotEnoughFreeSpaceIsReportedBeforeCreatingTheFile()
    {
        var error = await Assert.ThrowsAsync<IOException>(() => Create().RunAsync(_work.Path, false, 1L << 40, null, TestContext.Current.CancellationToken));

        Assert.Contains("espaço", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_work.BenchFolders());
    }

    [Fact]
    public void TheScoreWeighsSequentialSpeedAndHighQueueRandomAccess()
    {
        var result = new DiskBenchmarkResult("C:", 1024, 0, 0, 3000, 2000, 10000, 20000, 0, 400_000, 200_000, 0, 0, 0);

        Assert.Equal(2500 + 150 + 150, result.Score, 6);
        Assert.Equal(2, DiskBenchmarkResult.ScoreVersion);
    }

    [Fact]
    public void TheListedDrivesAreReadyAndHaveRoomForTheTest()
    {
        var drives = DiskBenchmark.Drives();

        Assert.NotEmpty(drives);
        Assert.All(drives, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Root));
            Assert.True(d.FreeBytes >= 1536L << 20);
        });
    }

    [Fact]
    public void AVeryShortRandomPhaseIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiskBenchmark(_logger, TimeSpan.FromMilliseconds(10)));
    }
}
