using M1vClock.Games.Bottleneck;

namespace M1vClock.Games.Tests;

public sealed class BottleneckClassificationTests
{
    private static LoadSample Sample(
        float? gpu = 40,
        float? cpu = 30,
        float? core = 50,
        double? vram = 50,
        float? ram = 60,
        float? gpuTemp = 60,
        float? cpuTemp = 60,
        bool cap = false) => new(gpu, cpu, core, vram, ram, gpuTemp, cpuTemp, cap);

    [Fact]
    public void WhenNothingIsNearALimitTheFramesAreCappedByTheGameOrVSync()
    {
        Assert.Equal(Limiter.Capped, Sample().Classify());
    }

    [Theory]
    [InlineData(97, Limiter.Vram)]
    [InlineData(99.5, Limiter.Vram)]
    [InlineData(96.9, Limiter.Capped)]
    public void FullVideoMemoryIsTheFirstRule(double vram, Limiter expected)
    {
        Assert.Equal(expected, Sample(vram: vram).Classify());
    }

    [Fact]
    public void FullVideoMemoryWinsOverEverythingElse()
    {
        Assert.Equal(Limiter.Vram, Sample(gpu: 100, cpu: 100, core: 100, vram: 98, ram: 99, gpuTemp: 99, cpuTemp: 99, cap: true).Classify());
    }

    [Theory]
    [InlineData(95, 60, false, Limiter.Thermal)]
    [InlineData(94.9, 60, false, Limiter.Capped)]
    [InlineData(60, 92, false, Limiter.Thermal)]
    [InlineData(60, 91.9, false, Limiter.Capped)]
    [InlineData(60, 60, true, Limiter.Thermal)]
    public void HeatOrAThrottleFlagIsReportedBeforeLoad(float cpuTemp, float gpuTemp, bool cap, Limiter expected)
    {
        Assert.Equal(expected, Sample(cpuTemp: cpuTemp, gpuTemp: gpuTemp, cap: cap).Classify());
    }

    [Fact]
    public void HeatWinsOverAGpuAtFullLoad()
    {
        Assert.Equal(Limiter.Thermal, Sample(gpu: 100, gpuTemp: 95).Classify());
    }

    [Theory]
    [InlineData(90, Limiter.Gpu)]
    [InlineData(100, Limiter.Gpu)]
    [InlineData(89.9, Limiter.Capped)]
    public void AGpuAtNinetyPercentOrMoreIsTheLimit(float gpu, Limiter expected)
    {
        Assert.Equal(expected, Sample(gpu: gpu).Classify());
    }

    [Theory]
    [InlineData(30, 85, Limiter.Cpu)]
    [InlineData(85, 40, Limiter.Cpu)]
    [InlineData(84.9, 84.9, Limiter.Capped)]
    public void ABusyCoreOrABusyCpuWithSpareGpuIsACpuBottleneck(float total, float core, Limiter expected)
    {
        Assert.Equal(expected, Sample(gpu: 60, cpu: total, core: core).Classify());
    }

    [Fact]
    public void AGpuAtTheLimitBeatsABusyCpu()
    {
        Assert.Equal(Limiter.Gpu, Sample(gpu: 97, cpu: 95, core: 100).Classify());
    }

    [Theory]
    [InlineData(92, Limiter.Ram)]
    [InlineData(91.9, Limiter.Capped)]
    public void FullRamIsTheLastRule(float ram, Limiter expected)
    {
        Assert.Equal(expected, Sample(ram: ram).Classify());
    }

    [Fact]
    public void MissingReadingsNeverCountAsALimit()
    {
        var empty = new LoadSample(null, null, null, null, null, null, null);

        Assert.Equal(Limiter.Capped, empty.Classify());
    }

    [Fact]
    public void ARealIdleSampleOfThisPcIsCapped()
    {
        var sample = new LoadSample(0, 9.87f, 23.97f, 1455.0 / 8176 * 100, 67.949036f, 42, null);

        Assert.Equal(Limiter.Capped, sample.Classify());
        Assert.Equal(17.8, sample.VramPercent!.Value, 1);
    }
}

public sealed class BottleneckSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static LoadSample Gpu() => new(97, 40, 60, 50, 60, 70, 60);

    private static LoadSample Cpu() => new(50, 60, 95, 50, 60, 70, 60);

    private static LoadSample Idle() => new(10, 10, 20, 50, 60, 50, 50);

    [Fact]
    public void ASessionNeedsADurationAboveZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BottleneckSession(Start, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BottleneckSession(Start, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void TheSessionIsDoneExactlyWhenTheDurationHasPassed()
    {
        var session = new BottleneckSession(Start, TimeSpan.FromMinutes(3));

        Assert.False(session.IsDone(Start + TimeSpan.FromSeconds(179)));
        Assert.True(session.IsDone(Start + TimeSpan.FromSeconds(180)));
        Assert.Equal(0.5, session.Progress(Start + TimeSpan.FromSeconds(90)), 3);
        Assert.Equal(1, session.Progress(Start + TimeSpan.FromMinutes(10)));
        Assert.Equal(0, session.Progress(Start - TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void TheDistributionIsInPercentSortedAndTheMainLimiterIsTheMostFrequent()
    {
        var session = new BottleneckSession(Start, TimeSpan.FromMinutes(1));
        for (var i = 0; i < 6; i++)
        {
            session.Add(Gpu());
        }

        for (var i = 0; i < 3; i++)
        {
            session.Add(Cpu());
        }

        session.Add(Idle());
        var report = session.Report();

        Assert.Equal(10, report.Samples);
        Assert.Equal(Limiter.Gpu, report.Main);
        Assert.Equal([new LimiterShare(Limiter.Gpu, 60), new LimiterShare(Limiter.Cpu, 30), new LimiterShare(Limiter.Capped, 10)], report.Distribution);
        Assert.Equal(100.0, report.Distribution.Sum(s => s.Percent), 6);
    }

    [Fact]
    public void ATieIsBrokenByTheOrderOfTheLimiters()
    {
        var session = new BottleneckSession(Start, TimeSpan.FromMinutes(1));
        session.Add(Cpu());
        session.Add(Gpu());

        Assert.Equal(Limiter.Gpu, session.Report().Main);
    }

    [Fact]
    public void AnEmptySessionHasNoMainLimiterAndNoNumbers()
    {
        var report = new BottleneckSession(Start, TimeSpan.FromMinutes(1)).Report();

        Assert.Equal(0, report.Samples);
        Assert.Null(report.Main);
        Assert.Empty(report.Distribution);
        Assert.Null(report.AverageGpuLoad);
        Assert.Null(report.PeakCpuCore);
        Assert.Null(report.AverageFps);
    }

    [Fact]
    public void AveragesAndPeaksIgnoreMissingReadings()
    {
        var session = new BottleneckSession(Start, TimeSpan.FromMinutes(1));
        session.Add(new LoadSample(80, 40, 70, 30, 50, 60, null, Fps: 100));
        session.Add(new LoadSample(null, 60, null, 90, 70, 80, 90, Fps: 0));
        session.Add(new LoadSample(100, null, 90, null, null, null, 70, Fps: 60));

        var report = session.Report();

        Assert.Equal(90, report.AverageGpuLoad);
        Assert.Equal(50, report.AverageCpuTotal);
        Assert.Equal(90, report.PeakCpuCore);
        Assert.Equal(90, report.PeakVram);
        Assert.Equal(70, report.PeakRam);
        Assert.Equal(80, report.PeakGpuTemperature);
        Assert.Equal(90, report.PeakCpuTemperature);
        Assert.Equal(80, report.AverageFps);
    }
}
