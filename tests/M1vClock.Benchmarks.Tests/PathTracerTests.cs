using System.Diagnostics;
using M1vClock.Benchmarks.Cpu;

namespace M1vClock.Benchmarks.Tests;

public sealed class PathTracerTests
{
    private const int Width = 64;
    private const int Height = 48;
    private const int Samples = 4;

    private static int[] Render(int threads)
    {
        var tracer = new PathTracer(Width, Height, Samples);
        tracer.Run(threads, TimeSpan.Zero, TestContext.Current.CancellationToken);
        var pixels = new int[tracer.Width * tracer.Height];
        tracer.CopyPixelsTo(pixels);
        return pixels;
    }

    [Fact]
    public void ADownsampledCopyWithFactorOneIsTheWholeImage()
    {
        var tracer = new PathTracer(Width, Height, Samples);
        tracer.Run(2, TimeSpan.Zero, TestContext.Current.CancellationToken);
        var full = new int[Width * Height];
        var same = new int[Width * Height];

        tracer.CopyPixelsTo(full);
        tracer.CopyDownsampledPixelsTo(same, 1);

        Assert.Equal(full, same);
    }

    [Fact]
    public void ADownsampledCopyWithFactorTwoKeepsEverySecondPixelOfEverySecondRow()
    {
        var tracer = new PathTracer(Width, Height, Samples);
        tracer.Run(2, TimeSpan.Zero, TestContext.Current.CancellationToken);
        var half = new int[Width / 2 * (Height / 2)];

        tracer.CopyDownsampledPixelsTo(half, 2);

        Assert.Equal(tracer.PixelAt(0, 0), half[0]);
        Assert.Equal(tracer.PixelAt(10, 6), half[3 * (Width / 2) + 5]);
        Assert.Equal(tracer.PixelAt(Width - 2, Height - 2), half[^1]);
    }

    [Fact]
    public void ADownsampledCopyIntoASmallBufferOrWithAZeroFactorIsRejected()
    {
        var tracer = new PathTracer(Width, Height, Samples);

        Assert.Throws<ArgumentOutOfRangeException>(() => tracer.CopyDownsampledPixelsTo(new int[10], 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracer.CopyDownsampledPixelsTo(new int[Width * Height], 0));
    }

    [Fact]
    public void TheDefaultsKeepTheSceneSizeAndSampleCountOfTheOriginal()
    {
        var tracer = new PathTracer();

        Assert.Equal(960, tracer.Width);
        Assert.Equal(540, tracer.Height);
        Assert.Equal(1024, tracer.SamplesPerPixel);
        Assert.Equal(960L * 540 * 1024, tracer.SamplesPerPass);
    }

    [Fact]
    public void TheSameImageComesOutWhateverTheNumberOfThreads()
    {
        var single = Render(1);
        var many = Render(Environment.ProcessorCount);

        Assert.Equal(single, many);
    }

    [Fact]
    public void TwoRunsOfTheSameSceneProduceTheSameImage()
    {
        Assert.Equal(Render(2), Render(2));
    }

    [Fact]
    public void TheImageIsNotBlankAndIsFullyOpaque()
    {
        var pixels = Render(2);

        Assert.Contains(pixels, p => (p & 0xFF) > 20);
        Assert.Contains(pixels, p => (p & 0xFF) < 200);
        Assert.All(pixels, p => Assert.Equal(0xFF, (p >> 24) & 0xFF));
    }

    [Fact]
    public void EveryPixelIsGrayBecauseTheSceneIsMonochrome()
    {
        Assert.All(Render(2), p => Assert.Equal(p & 0xFF, (p >> 8) & 0xFF));
    }

    [Fact]
    public void ThePassDoesTheSameAmountOfWorkNoMatterWhoRunsIt()
    {
        var tracer = new PathTracer(Width, Height, Samples);

        var one = tracer.Run(1, TimeSpan.Zero, TestContext.Current.CancellationToken);
        var four = tracer.Run(4, TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.Equal(tracer.SamplesPerPass, one.Samples);
        Assert.Equal(one.Samples, four.Samples);
        Assert.Equal(1, one.Passes);
        Assert.Equal(1, four.Passes);
    }

    [Fact]
    public void AMinimumDurationRepeatsWholePassesAndCountsThemAll()
    {
        var tracer = new PathTracer(32, 32, 2);
        var seen = 0;

        var result = tracer.Run(2, TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken, () => seen++);

        Assert.True(result.Passes > 1 || result.Seconds / result.Passes >= 0.4);
        Assert.Equal(result.Passes, seen);
        Assert.Equal(tracer.SamplesPerPass * result.Passes, result.Samples);
        Assert.True(result.Seconds >= 0.4);
    }

    [Fact]
    public void TheScoreIsSamplesPerSecondOverTenThousand()
    {
        var result = new PathTraceResult(8, 2, 400_000_000, 1);

        Assert.Equal(20_000, result.Score, 6);
        Assert.Equal(0, new PathTraceResult(8, 0, 1, 1).Score);
    }

    [Fact]
    public void CancellingStopsTheRunWithACancellation()
    {
        var tracer = new PathTracer(128, 128, 64);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        Assert.ThrowsAny<OperationCanceledException>(() => tracer.Run(2, TimeSpan.Zero, cancellation.Token));
    }

    [Fact]
    public void TheBucketsCoverEveryPixelExactlyOnceAndStartAtTheCenter()
    {
        var tracer = new PathTracer();
        var covered = new int[tracer.Width * tracer.Height];

        foreach (var bucket in tracer.Buckets)
        {
            for (var y = bucket.Y; y < bucket.Y + bucket.Height; y++)
            {
                for (var x = bucket.X; x < bucket.X + bucket.Width; x++)
                {
                    covered[y * tracer.Width + x]++;
                }
            }
        }

        Assert.All(covered, c => Assert.Equal(1, c));
        var first = tracer.Buckets[0];
        Assert.InRange(first.X + first.Width / 2, tracer.Width / 2 - 32, tracer.Width / 2 + 32);
        Assert.InRange(first.Y + first.Height / 2, tracer.Height / 2 - 32, tracer.Height / 2 + 32);
        Assert.Equal(tracer.BucketsTotal, tracer.Buckets.Count);
    }

    [Fact]
    public void BucketsAtTheEdgeAreCutToTheImage()
    {
        var tracer = new PathTracer(70, 40, 1);

        Assert.All(tracer.Buckets, b =>
        {
            Assert.True(b.X + b.Width <= 70);
            Assert.True(b.Y + b.Height <= 40);
        });
    }

    [Fact]
    public async Task ProgressIsVisibleWhileRenderingAndTheActiveBucketsAreReported()
    {
        var tracer = new PathTracer(256, 256, 32);
        var sawActive = false;
        var sawPartial = false;
        var run = Task.Run(() => tracer.Run(2, TimeSpan.Zero, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);

        var guard = Stopwatch.StartNew();
        while (!run.IsCompleted && guard.Elapsed < TimeSpan.FromSeconds(30))
        {
            sawActive |= tracer.ActiveBuckets().Count > 0;
            sawPartial |= tracer.BucketsDone > 0 && tracer.BucketsDone < tracer.BucketsTotal;
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        await run;
        Assert.True(sawActive);
        Assert.True(sawPartial);
        Assert.Equal(tracer.BucketsTotal, tracer.BucketsDone);
        Assert.Empty(tracer.ActiveBuckets());
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 10, 0)]
    public void ASceneWithNothingToRenderIsRejected(int width, int height, int samples)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PathTracer(width, height, samples));
    }

    [Fact]
    public void AtLeastOneThreadIsRequired()
    {
        var tracer = new PathTracer(8, 8, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => tracer.Run(0, TimeSpan.Zero, TestContext.Current.CancellationToken));
    }
}
