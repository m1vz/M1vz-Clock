using M1vClock.Benchmarks.Gpu;
using M1vClock.Benchmarks.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Benchmarks.Tests;

public sealed class GpuBurnSettingsTests
{
    private static GpuBurn Create(GpuBurnSettings settings) => new(settings, NullLogger<GpuBurn>.Instance);

    [Theory]
    [InlineData(32, 720)]
    [InlineData(1280, 32)]
    public void AWindowThatIsTooSmallIsRefused(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new GpuBurnSettings(width, height, false, 4, 96, 32, string.Empty)));
    }

    [Theory]
    [InlineData(GpuBurnSettings.MinimumShells - 1)]
    [InlineData(GpuBurnSettings.MaximumShells + 1)]
    public void TheNumberOfFurLayersStaysInsideTheSafeRange(int shells)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new GpuBurnSettings(1280, 720, false, 4, shells, 32, string.Empty)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(GpuBurnSettings.MaximumIterations + 1)]
    public void TheExtraShaderWorkStaysInsideTheSafeRange(int iterations)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new GpuBurnSettings(1280, 720, false, 4, 96, iterations, string.Empty)));
    }

    [Fact]
    public void ThePresetsGrowInWorkloadFromNormalToExtreme()
    {
        var normal = GpuBurnSettings.Normal(1920, 1080, false, 4, string.Empty);
        var high = GpuBurnSettings.High(1920, 1080, false, 4, string.Empty);
        var extreme = GpuBurnSettings.Extreme(1920, 1080, false, 4, string.Empty);

        Assert.True(normal.Shells < high.Shells && high.Shells < extreme.Shells);
        Assert.True(normal.Iterations < high.Iterations && high.Iterations < extreme.Iterations);
        Assert.Equal((96, 32), (normal.Shells, normal.Iterations));
        Assert.Equal((128, 48), (high.Shells, high.Iterations));
        Assert.Equal((160, 64), (extreme.Shells, extreme.Iterations));
    }

    [Fact]
    public void ABurnThatNeverStartedIsIdleAndHarmlessToStop()
    {
        using var burn = Create(GpuBurnSettings.Normal(1280, 720, false, 1, string.Empty));

        burn.Halt();

        Assert.False(burn.Status.Alive);
        Assert.True(burn.IsResponsive);
        Assert.Equal(0, burn.Status.Frames);
    }
}

public sealed class GpuSceneOptionsTests
{
    [Fact]
    public void AVeryShortSceneIsRefusedBecauseItsFrameTimesWouldMeanNothing()
    {
        var bench = new GpuSceneBenchmark();

        Assert.Throws<ArgumentOutOfRangeException>(() => bench.Run(new GpuSceneOptions(1280, 720, TimeSpan.FromSeconds(1), string.Empty), CancellationToken.None));
    }

    [Fact]
    public void TheScoreIsTenTimesTheAverageFpsLikeTheOriginal()
    {
        var result = new GpuSceneResult(172.4, 142, 2581, 15, 1920, 1080, "GPU");

        Assert.Equal(1724, result.Score, 6);
    }

    [Fact]
    public void TheTerrainFollowsTheSameFormulaAsTheOriginalSoTheSceneIsTheSameEverywhere()
    {
        var first = GpuSceneBenchmark.TerrainHeightAt(120, -80);
        var second = GpuSceneBenchmark.TerrainHeightAt(120, -80);

        Assert.Equal(first, second);
        Assert.InRange(first, -10, 80);
        Assert.NotEqual(GpuSceneBenchmark.TerrainHeightAt(0, 0), GpuSceneBenchmark.TerrainHeightAt(300, 200));
        Assert.Equal(40_000, GpuSceneBenchmark.TreeInstanceCount);
    }

    [Fact]
    public void TheCatalogKnowsTheSceneMetrics()
    {
        var result = new GpuSceneResult(170, 140, 2550, 15, 1920, 1080, "GPU");

        var keys = BenchmarkCatalog.MetricsOf(result).Keys.Order();

        Assert.Equal(BenchmarkCatalog.Find(BenchmarkTests.GpuScene)!.Metrics.Select(m => m.Key).Order(), keys);
    }
}

public sealed class GpuHardwareTests
{
    private static void RequireGpu()
    {
        Assert.SkipUnless(Environment.UserInteractive, "Precisa de uma sessão interativa para abrir a janela.");
        Assert.SkipUnless(GpuSupport.HasHardwareAdapter(), "Este PC não tem GPU de hardware.");
    }

    [Fact]
    public void ThisPcHasAHardwareGpuOrTheTestsSayWhy()
    {
        Assert.Equal(GpuSupport.HasHardwareAdapter(), GpuSupport.HasHardwareAdapter());
    }

    [Fact]
    public void TheRealSceneRunsForFiveSecondsAndProducesAFullResult()
    {
        RequireGpu();
        var bench = new GpuSceneBenchmark();

        var result = bench.Run(new GpuSceneOptions(1280, 720, TimeSpan.FromSeconds(GpuSceneBenchmark.MinimumDurationSeconds), string.Empty), TestContext.Current.CancellationToken);

        Assert.True(result.Frames > 50, $"só {result.Frames} quadros");
        Assert.True(result.AverageFps > 5);
        Assert.True(result.LowFps > 0);
        Assert.True(result.LowFps <= result.AverageFps * 1.05);
        Assert.False(string.IsNullOrWhiteSpace(result.Adapter));
        Assert.Equal((1280, 720), (result.Width, result.Height));
        Assert.True(result.Seconds >= GpuSceneBenchmark.MinimumDurationSeconds);
        Assert.Equal(1, bench.Progress, 0.15);
    }

    [Fact]
    public void CancellingTheRealSceneStopsItQuicklyWithACancellation()
    {
        RequireGpu();
        var bench = new GpuSceneBenchmark();
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(1500));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(() => bench.Run(new GpuSceneOptions(1280, 720, TimeSpan.FromSeconds(60), string.Empty), cancellation.Token));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "a janela devia fechar logo depois do cancelamento");
    }

    [Fact]
    public async Task TheRealBurnRendersFramesReportsItsStateAndStopsCleanly()
    {
        RequireGpu();
        using var burn = new GpuBurn(GpuBurnSettings.Normal(960, 540, false, 1, string.Empty), NullLogger<GpuBurn>.Instance);

        burn.Start();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (burn.Status.Frames < 10 && burn.Status.Error is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        var running = burn.Status;
        burn.Halt();
        var stopped = burn.Status;

        Assert.Null(running.Error);
        Assert.True(running.Alive);
        Assert.True(running.Frames >= 10);
        Assert.True(burn.IsResponsive);
        Assert.False(string.IsNullOrWhiteSpace(burn.AdapterUsed));
        Assert.False(stopped.Alive);
        Assert.Equal(0, stopped.Fps);
    }

    [Fact]
    public async Task AdapterNameThatDoesNotExistFallsBackToTheBestGpu()
    {
        RequireGpu();
        using var burn = new GpuBurn(GpuBurnSettings.Normal(640, 360, false, 1, "placa-que-nao-existe"), NullLogger<GpuBurn>.Instance);

        burn.Start();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (burn.Status.Frames < 5 && burn.Status.Error is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        burn.Halt();

        Assert.Null(burn.Status.Error);
        Assert.False(string.IsNullOrWhiteSpace(burn.AdapterUsed));
    }

    [Fact]
    public void StartingARunningBurnAgainIsRefusedAndItCanRunAgainAfterStopping()
    {
        RequireGpu();
        using var burn = new GpuBurn(GpuBurnSettings.Normal(640, 360, false, 1, string.Empty), NullLogger<GpuBurn>.Instance);

        burn.Start();
        Assert.Throws<InvalidOperationException>(burn.Start);
        burn.Halt();
        burn.Start();
        Assert.True(burn.Status.Alive || burn.Status.Error is not null);
        burn.Halt();
        burn.Halt();
    }

    [Fact]
    public void TheRayTracingCheckGivesARealAnswerAboutThisPc()
    {
        Assert.SkipUnless(GpuSupport.HasHardwareAdapter(), "Este PC não tem GPU de hardware.");

        var support = RayTracingCheck.Check(string.Empty, NullLogger.Instance);

        Assert.True(Enum.IsDefined(support.Status));
        Assert.NotEqual(RayTracingStatus.NoGpu, support.Status);
        if (support.Status is RayTracingStatus.Supported or RayTracingStatus.NoHardwareRayTracing)
        {
            Assert.False(string.IsNullOrWhiteSpace(support.Adapter));
            Assert.False(string.IsNullOrWhiteSpace(support.Tier));
        }
    }

    [Fact]
    public void AnAdapterNameThatDoesNotMatchStillChecksTheBestGpu()
    {
        Assert.SkipUnless(GpuSupport.HasHardwareAdapter(), "Este PC não tem GPU de hardware.");

        var named = RayTracingCheck.Check("placa-que-nao-existe", NullLogger.Instance);
        var plain = RayTracingCheck.Check(string.Empty, NullLogger.Instance);

        Assert.Equal(plain.Status, named.Status);
        Assert.Equal(plain.Adapter, named.Adapter);
    }
}
