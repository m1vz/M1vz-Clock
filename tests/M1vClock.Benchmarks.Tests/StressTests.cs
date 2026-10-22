using System.Diagnostics;
using M1vClock.Benchmarks.Safety;
using M1vClock.Benchmarks.Stress;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Benchmarks.Tests;

internal sealed class FakeWorkload : IStressWorkload
{
    private readonly Lock _gate = new();
    private WorkloadStatus _status = WorkloadStatus.Idle;

    public int Starts { get; private set; }

    public int Halts { get; private set; }

    public WorkloadStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
        set
        {
            lock (_gate)
            {
                _status = value;
            }
        }
    }

    public void Start()
    {
        Starts++;
        Status = Status with { Alive = true };
    }

    public void Halt()
    {
        Halts++;
        Status = Status with { Alive = false };
    }

    public void Dispose() => Halt();
}

public sealed class StressRunnerTests
{
    private static StressRunner Create(bool cpu, bool ram, double fraction = 0.6, long available = 1500L << 20) =>
        new(cpu, ram, fraction, new CollectingLogger<StressRunner>(), () => available);

    [Theory]
    [InlineData(8L << 30, 0.6, 4864L << 20)]
    [InlineData(8L << 30, 0.95, 6528L << 20)]
    [InlineData(8L << 30, 0.01, 768L << 20)]
    [InlineData(2L << 30, 0.8, 1024L << 20)]
    public void ThePlannedMemoryIsAFractionInWholeChunksAndAlwaysLeavesRoomForWindows(long available, double fraction, long expected)
    {
        var planned = StressRunner.PlannedRamBytes(available, fraction);

        Assert.Equal(expected, planned);
        Assert.True(available - planned >= StressRunner.SystemReserveBytes);
        Assert.Equal(0, planned % StressRunner.ChunkBytes);
    }

    [Fact]
    public void WithAlmostNothingFreeNothingIsPlanned()
    {
        Assert.True(StressRunner.PlannedRamBytes(1100L << 20, 0.8) < StressRunner.ChunkBytes);
    }

    [Fact]
    public async Task CpuStressKeepsSeveralCoresBusyAndStopsCleanly()
    {
        using var runner = Create(cpu: true, ram: false);
        var process = Process.GetCurrentProcess();
        var before = process.TotalProcessorTime;

        runner.Start();
        await Task.Delay(700, TestContext.Current.CancellationToken);
        process.Refresh();
        var busy = process.TotalProcessorTime - before;
        var running = runner.Status;
        runner.Halt();

        Assert.True(running.Alive);
        Assert.Equal(Environment.ProcessorCount, running.CpuThreads);
        Assert.True(busy > TimeSpan.FromSeconds(1), $"só {busy.TotalSeconds:0.0} s de CPU em 0,7 s");
        Assert.False(runner.Status.Alive);
        Assert.Equal(0, runner.Status.CpuThreads);
    }

    [Fact]
    public async Task RamStressWritesAndVerifiesWithoutErrorsAndGivesTheMemoryBack()
    {
        using var runner = Create(cpu: false, ram: true, fraction: 0.8, available: 1500L << 20);
        runner.Start();

        var guard = Stopwatch.StartNew();
        while (runner.Status.MemoryPasses < 2 && guard.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        var running = runner.Status;
        runner.Halt();

        Assert.True(running.MemoryPasses >= 2);
        Assert.Equal(0, running.MemoryErrors);
        Assert.Equal(3 * StressRunner.ChunkBytes, running.MemoryBytes);
        Assert.Equal(0, runner.Status.MemoryBytes);
    }

    [Fact]
    public async Task CpuAndRamTogetherLeaveOneCoreForTheMemoryWorker()
    {
        using var runner = Create(cpu: true, ram: true, available: 1500L << 20);

        runner.Start();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var running = runner.Status;
        runner.Halt();

        Assert.Equal(Environment.ProcessorCount - 1, running.CpuThreads);
    }

    [Fact]
    public async Task WhenThereIsNotEnoughMemoryTheRamPartDoesNotStartAndIsLogged()
    {
        var logger = new CollectingLogger<StressRunner>();
        using var runner = new StressRunner(false, true, 0.6, logger, () => 1100L << 20);

        runner.Start();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(0, runner.Status.MemoryBytes);
        Assert.Contains(logger.Entries, e => e.EventId.Id == 4206);
        runner.Halt();
    }

    [Fact]
    public void StartingTwiceIsRefusedAndStoppingTwiceIsHarmless()
    {
        using var runner = Create(cpu: true, ram: false);
        runner.Start();

        Assert.Throws<InvalidOperationException>(runner.Start);

        runner.Halt();
        runner.Halt();
        Assert.False(runner.Status.Alive);
    }

    [Fact]
    public void ARunnerCanBeStartedAgainAfterStopping()
    {
        using var runner = Create(cpu: true, ram: false);

        runner.Start();
        runner.Halt();
        runner.Start();

        Assert.True(runner.Status.Alive);
        runner.Halt();
    }

    [Fact]
    public void TheRamFractionIsClampedToTheSafeRange()
    {
        var generous = StressRunner.PlannedRamBytes(8L << 30, 5);
        var stingy = StressRunner.PlannedRamBytes(8L << 30, -1);

        Assert.Equal(StressRunner.PlannedRamBytes(8L << 30, StressRunner.MaximumRamFraction), generous);
        Assert.Equal(StressRunner.PlannedRamBytes(8L << 30, StressRunner.MinimumRamFraction), stingy);
    }
}

public sealed class StressSessionTests : IAsyncDisposable
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(250);

    private readonly FakeTimeProvider _clock = new(Samples.Now);
    private readonly FakeProbe _probe = new();
    private readonly SafetyMonitor _monitor;
    private readonly StressSession _session;
    private readonly FakeWorkload _cpuRam = new();
    private readonly FakeWorkload _gpu = new();

    public StressSessionTests()
    {
        _monitor = new SafetyMonitor(_probe, _clock, new CollectingLogger<SafetyMonitor>());
        _session = new StressSession(_monitor, _clock, new CollectingLogger<StressSession>());
    }

    public async ValueTask DisposeAsync() => await _session.DisposeAsync();

    private static StressPlan Plan(bool cpu = true, bool ram = false, bool gpu = false, double minutes = 1) =>
        new(cpu, ram, gpu, TimeSpan.FromMinutes(minutes), new SafetyLimits(85, 80, TimeSpan.FromSeconds(1)));

    private async Task<StressOutcome> DriveAsync(TimeSpan? limit = null)
    {
        var completion = _session.Completion ?? throw new InvalidOperationException("A sessão não começou.");
        var guard = Stopwatch.StartNew();
        var simulated = TimeSpan.Zero;
        while (!completion.IsCompleted)
        {
            Assert.True(guard.Elapsed < TimeSpan.FromSeconds(20), "A sessão não terminou.");
            if (simulated < (limit ?? TimeSpan.FromMinutes(5)))
            {
                _clock.Advance(Step);
                simulated += Step;
            }

            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        return await completion;
    }

    [Fact]
    public void NothingSelectedDoesNotStart()
    {
        var result = _session.Start(Plan(cpu: false), _cpuRam, _gpu);

        Assert.Equal(StressStartStatus.NothingSelected, result.Status);
        Assert.Equal(0, _cpuRam.Starts);
    }

    [Fact]
    public void ACpuTestWithoutATemperatureSensorIsRefusedAndNothingStarts()
    {
        _probe.Steady(ThermalComponent.Cpu, null);

        var result = _session.Start(Plan(), _cpuRam, null);

        Assert.Equal(StressStartStatus.NoTemperatureSensor, result.Status);
        Assert.Equal(ThermalComponent.Cpu, result.MissingSensor);
        Assert.Equal(0, _cpuRam.Starts);
        Assert.False(_monitor.IsRunning);
    }

    [Fact]
    public void AGpuTestWithoutAGpuTemperatureIsRefusedEvenIfTheCpuHasOne()
    {
        _probe.Steady(ThermalComponent.Cpu, 50).Steady(ThermalComponent.Gpu, null);

        var result = _session.Start(Plan(gpu: true), _cpuRam, _gpu);

        Assert.Equal(StressStartStatus.NoTemperatureSensor, result.Status);
        Assert.Equal(ThermalComponent.Gpu, result.MissingSensor);
        Assert.Equal(0, _gpu.Starts);
        Assert.Equal(0, _cpuRam.Starts);
    }

    [Fact]
    public async Task ARamOnlyTestDoesNotNeedATemperatureSensor()
    {
        _probe.Steady(ThermalComponent.Cpu, null);

        var result = _session.Start(Plan(cpu: false, ram: true, minutes: 0.05), _cpuRam, null);
        var outcome = await DriveAsync();

        Assert.Equal(StressStartStatus.Started, result.Status);
        Assert.Equal(StressEnd.Completed, outcome.End);
    }

    [Fact]
    public async Task ATestRunsForItsDurationAndThenEndsAsCompleted()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);

        _session.Start(Plan(minutes: 1), _cpuRam, null);
        var outcome = await DriveAsync();

        Assert.Equal(StressEnd.Completed, outcome.End);
        Assert.True(outcome.Elapsed >= TimeSpan.FromMinutes(1));
        Assert.Null(outcome.Trip);
        Assert.Equal(60, outcome.CpuMaximum);
        Assert.Equal(1, _cpuRam.Starts);
        Assert.Equal(1, _cpuRam.Halts);
    }

    [Fact]
    public async Task ReachingTheTemperatureLimitStopsEverythingAndSaysWhy()
    {
        _probe.Steady(ThermalComponent.Cpu, 60).Steady(ThermalComponent.Gpu, 50);
        _probe.Then(ThermalComponent.Cpu, 60, 70, 86);

        _session.Start(Plan(gpu: true, minutes: 30), _cpuRam, _gpu);
        var outcome = await DriveAsync();

        Assert.Equal(StressEnd.Temperature, outcome.End);
        Assert.Equal(ThermalComponent.Cpu, outcome.Trip?.Component);
        Assert.Equal(86, outcome.CpuMaximum);
        Assert.Equal(50, outcome.GpuMaximum);
        Assert.Equal(1, _cpuRam.Halts);
        Assert.Equal(1, _gpu.Halts);
        Assert.True(outcome.Elapsed < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task MemoryErrorsFromTheWorkloadEndTheTest()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        _session.Start(Plan(ram: true, minutes: 30), _cpuRam, null);
        _cpuRam.Status = _cpuRam.Status with { MemoryErrors = 12, MemoryPasses = 4 };

        var outcome = await DriveAsync();

        Assert.Equal(StressEnd.MemoryErrors, outcome.End);
        Assert.Equal(12, outcome.CpuRam.MemoryErrors);
        Assert.Equal(4, outcome.CpuRam.MemoryPasses);
    }

    [Fact]
    public async Task TheUserStoppingEndsTheTestAndHaltsTheWorkloads()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        _session.Start(Plan(minutes: 0), _cpuRam, null);

        _session.Stop();
        var outcome = await DriveAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(StressEnd.StoppedByUser, outcome.End);
        Assert.Equal(1, _cpuRam.Halts);
    }

    [Fact]
    public async Task StoppingAgainAfterTheRunEndedIsHarmless()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        _session.Start(Plan(minutes: 0.05), _cpuRam, null);
        await DriveAsync();

        for (var i = 0; i < 50; i++)
        {
            _session.Stop();
        }

        Assert.False(_session.IsRunning);
        Assert.Equal(1, _cpuRam.Halts);
    }

    [Fact]
    public async Task StoppingDoesNotWaitForTheNextClockTick()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        _session.Start(Plan(minutes: 0), _cpuRam, null);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        _session.Stop();
        var outcome = await _session.Completion!.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(StressEnd.StoppedByUser, outcome.End);
        Assert.Equal(1, _cpuRam.Halts);
    }

    [Fact]
    public async Task ATestWithoutADurationRunsUntilSomeoneStopsIt()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        _session.Start(Plan(minutes: 0), _cpuRam, null);

        _clock.Advance(TimeSpan.FromHours(3));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.True(_session.IsRunning);
        _session.Stop();
        Assert.Equal(StressEnd.StoppedByUser, (await DriveAsync(TimeSpan.FromSeconds(2))).End);
    }

    [Fact]
    public async Task AGpuWorkloadThatReportsAnErrorEndsTheTestAsFailed()
    {
        _probe.Steady(ThermalComponent.Cpu, 60).Steady(ThermalComponent.Gpu, 55);
        _session.Start(Plan(cpu: false, gpu: true, minutes: 30), null, _gpu);
        _gpu.Status = _gpu.Status with { Error = "dispositivo perdido" };

        var outcome = await DriveAsync();

        Assert.Equal(StressEnd.WorkloadFailed, outcome.End);
        Assert.Equal("dispositivo perdido", outcome.Gpu.Error);
    }

    [Fact]
    public async Task AGpuWindowClosedByTheUserEndsTheTestAsStoppedByUser()
    {
        _probe.Steady(ThermalComponent.Gpu, 55);
        _session.Start(Plan(cpu: false, gpu: true, minutes: 30), null, _gpu);
        _gpu.Status = _gpu.Status with { Alive = false };

        var outcome = await DriveAsync();

        Assert.Equal(StressEnd.StoppedByUser, outcome.End);
    }

    [Fact]
    public async Task AFrozenWindowEndsTheTest()
    {
        _probe.Steady(ThermalComponent.Gpu, 55);
        _session.Start(Plan(cpu: false, gpu: true, minutes: 30), null, _gpu, () => false);

        var outcome = await DriveAsync();

        Assert.Equal(StressEnd.WindowUnresponsive, outcome.End);
    }

    [Fact]
    public async Task ASecondTestCannotStartWhileOneIsRunning()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        _session.Start(Plan(minutes: 0), _cpuRam, null);

        var second = _session.Start(Plan(minutes: 0), new FakeWorkload(), null);

        Assert.Equal(StressStartStatus.AlreadyRunning, second.Status);
        _session.Stop();
        await DriveAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ProgressIsReportedWhileTheTestRuns()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        var updates = new List<StressProgress>();
        _session.Progress += (_, p) =>
        {
            lock (updates)
            {
                updates.Add(p);
            }
        };

        _session.Start(Plan(minutes: 0.1), _cpuRam, null);
        await DriveAsync();

        lock (updates)
        {
            Assert.True(updates.Count >= 5);
            Assert.True(updates[^1].Elapsed > updates[0].Elapsed);
            Assert.All(updates, u => Assert.Equal(TimeSpan.FromMinutes(0.1), u.Duration));
        }
    }
}
