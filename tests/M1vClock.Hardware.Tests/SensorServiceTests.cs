using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Hardware.Tests;

internal sealed class FakeSensorSource : ISensorSource
{
    private int _opens;
    private int _reads;

    public int OpenCalls => Volatile.Read(ref _opens);

    public int ReadCalls => Volatile.Read(ref _reads);

    public bool Disposed { get; private set; }

    public Exception? OpenFailure { get; set; }

    public Exception? ReadFailure { get; set; }

    public int ConcurrentReads { get; private set; }

    public int MaxConcurrentReads { get; private set; }

    public IReadOnlyList<SensorReading> Readings { get; set; } = [Reading("cpu/0/temp", SensorHardware.Cpu, "CPU Package", SensorKind.Temperature, 55f)];

    public static SensorReading Reading(string id, SensorHardware hardware, string name, SensorKind kind, float? value) =>
        new(id, "Hardware", hardware, name, kind, value, value, value);

    public void Open()
    {
        Interlocked.Increment(ref _opens);
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }
    }

    public IReadOnlyList<SensorReading> Read()
    {
        lock (this)
        {
            ConcurrentReads++;
            MaxConcurrentReads = Math.Max(MaxConcurrentReads, ConcurrentReads);
        }

        try
        {
            Interlocked.Increment(ref _reads);
            return ReadFailure is null ? Readings : throw ReadFailure;
        }
        finally
        {
            lock (this)
            {
                ConcurrentReads--;
            }
        }
    }

    public void Dispose() => Disposed = true;
}

public sealed class SensorServiceTests : IDisposable
{
    private readonly FakeSensorSource _source = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly CollectingLogger<SensorService> _logger = new();

    public void Dispose() => _source.Dispose();

    private SensorService Create() => new(_source, _clock, _logger);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    private async Task TickAsync(int expectedReads)
    {
        _clock.Advance(SensorService.Interval);
        await WaitUntilAsync(() => _source.ReadCalls >= expectedReads);
    }

    [Fact]
    public void WithoutSubscribersNothingIsOpenedOrRead()
    {
        using var service = Create();

        _clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(0, _source.OpenCalls);
        Assert.Equal(0, _source.ReadCalls);
        Assert.Equal(SensorServiceState.Stopped, service.State);
        Assert.Null(service.Latest);
    }

    [Fact]
    public async Task SubscribingOpensTheSourceAndPublishesTheFirstSnapshotAtOnce()
    {
        using var service = Create();
        var published = new List<SensorSnapshot>();
        service.SnapshotReady += (_, snapshot) => published.Add(snapshot);

        using var lease = service.Subscribe();
        await WaitUntilAsync(() => service.Latest is not null);

        Assert.Equal(1, _source.OpenCalls);
        Assert.Equal(SensorServiceState.Running, service.State);
        Assert.Equal(_clock.GetUtcNow(), service.Latest!.At);
        Assert.Equal(55f, service.Latest.CpuTemperature);
        Assert.Single(published);
    }

    [Fact]
    public async Task ASnapshotIsPublishedEverySecond()
    {
        using var service = Create();
        using var lease = service.Subscribe();
        await WaitUntilAsync(() => _source.ReadCalls >= 1);

        await TickAsync(2);
        await TickAsync(3);

        Assert.Equal(3, _source.ReadCalls);
        Assert.Equal(1, _source.OpenCalls);
    }

    [Fact]
    public async Task TheServiceStopsOnlyWhenTheLastSubscriberLeaves()
    {
        using var service = Create();
        var first = service.Subscribe();
        var second = service.Subscribe();
        await WaitUntilAsync(() => _source.ReadCalls >= 1);

        first.Dispose();
        await TickAsync(2);
        second.Dispose();
        await WaitUntilAsync(() => service.State == SensorServiceState.Stopped);
        var reads = _source.ReadCalls;
        _clock.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(reads, _source.ReadCalls);
    }

    [Fact]
    public async Task DisposingALeaseTwiceDoesNotStealAnotherSubscribersSlot()
    {
        using var service = Create();
        var first = service.Subscribe();
        using var second = service.Subscribe();
        await WaitUntilAsync(() => _source.ReadCalls >= 1);

        first.Dispose();
        first.Dispose();
        await TickAsync(2);

        Assert.Equal(SensorServiceState.Running, service.State);
    }

    [Fact]
    public async Task PausingStopsReadingAndResumingContinuesWithoutReopening()
    {
        using var service = Create();
        using var lease = service.Subscribe();
        await WaitUntilAsync(() => _source.ReadCalls >= 1);

        var pause = service.Pause();
        await WaitUntilAsync(() => service.State == SensorServiceState.Paused);
        var reads = _source.ReadCalls;
        _clock.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(reads, _source.ReadCalls);

        pause.Dispose();
        await WaitUntilAsync(() => _source.ReadCalls > reads);

        Assert.Equal(SensorServiceState.Running, service.State);
        Assert.Equal(1, _source.OpenCalls);
        Assert.Equal(1, _source.MaxConcurrentReads);
    }

    [Fact]
    public async Task RapidPauseAndResumeNeverReadsFromTwoLoopsAtOnce()
    {
        using var service = Create();
        using var lease = service.Subscribe();
        await WaitUntilAsync(() => _source.ReadCalls >= 1);

        for (var i = 0; i < 20; i++)
        {
            service.Pause().Dispose();
            _clock.Advance(SensorService.Interval);
        }

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(1, _source.MaxConcurrentReads);
    }

    [Fact]
    public async Task AnOpenFailureMarksTheServiceAsFailedAndIsLogged()
    {
        _source.OpenFailure = new InvalidOperationException("Driver recusado.");
        using var service = Create();

        using var lease = service.Subscribe();
        await WaitUntilAsync(() => service.State == SensorServiceState.Failed);

        Assert.Equal(0, _source.ReadCalls);
        var entry = Assert.Single(_logger.Entries, e => e.EventId.Id == 3201);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        using var another = service.Subscribe();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, _source.OpenCalls);
        Assert.Equal(SensorServiceState.Failed, service.State);
    }

    [Fact]
    public async Task AFailedReadSkipsTheTickIsLoggedOnceAndTheNextGoodReadPublishes()
    {
        using var service = Create();
        using var lease = service.Subscribe();
        await WaitUntilAsync(() => _source.ReadCalls >= 1);
        _source.ReadFailure = new IOException("Disco ocupado.");

        await TickAsync(2);
        await TickAsync(3);
        _source.ReadFailure = null;
        await TickAsync(4);

        Assert.Single(_logger.Entries, e => e.EventId.Id == 3202);
        await WaitUntilAsync(() => service.Latest!.At == _clock.GetUtcNow());
    }

    [Fact]
    public async Task DisposingStopsTheLoopAndReleasesTheSource()
    {
        var service = Create();
        var lease = service.Subscribe();
        await WaitUntilAsync(() => _source.ReadCalls >= 1);

        service.Dispose();
        var reads = _source.ReadCalls;
        _clock.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        lease.Dispose();

        Assert.True(_source.Disposed);
        Assert.Equal(reads, _source.ReadCalls);
        Assert.Equal(SensorServiceState.Stopped, service.State);
        service.Dispose();
    }

    [Fact]
    public async Task StateChangesAreAnnounced()
    {
        using var service = Create();
        var states = new List<SensorServiceState>();
        service.StateChanged += (_, _) => states.Add(service.State);

        var lease = service.Subscribe();
        await WaitUntilAsync(() => service.State == SensorServiceState.Running);
        lease.Dispose();
        await WaitUntilAsync(() => service.State == SensorServiceState.Stopped);

        Assert.Contains(SensorServiceState.Starting, states);
        Assert.Contains(SensorServiceState.Running, states);
        Assert.Equal(SensorServiceState.Stopped, states[^1]);
    }
}

public sealed class SensorSnapshotTests
{
    private static SensorReading R(SensorHardware hardware, string name, SensorKind kind, float? value) =>
        FakeSensorSource.Reading($"{hardware}/{name}/{kind}", hardware, name, kind, value);

    private static SensorSnapshot Snapshot(params SensorReading[] readings) => new(DateTimeOffset.UnixEpoch, readings);

    [Fact]
    public void TheCpuTemperaturePrefersThePackageSensor()
    {
        var snapshot = Snapshot(
            R(SensorHardware.Cpu, "Core #1", SensorKind.Temperature, 71),
            R(SensorHardware.Cpu, "CPU Package", SensorKind.Temperature, 64),
            R(SensorHardware.Cpu, "Core #2", SensorKind.Temperature, 69));

        Assert.Equal(64f, snapshot.CpuTemperature);
    }

    [Fact]
    public void AmdTctlAndTdieCountAsThePackage()
    {
        Assert.Equal(58f, Snapshot(R(SensorHardware.Cpu, "Core (Tctl/Tdie)", SensorKind.Temperature, 58), R(SensorHardware.Cpu, "CCD1", SensorKind.Temperature, 70)).CpuTemperature);
    }

    [Fact]
    public void WithoutAPackageSensorTheHottestCoreIsUsed()
    {
        var snapshot = Snapshot(R(SensorHardware.Cpu, "Core #1", SensorKind.Temperature, 61), R(SensorHardware.Cpu, "Core #2", SensorKind.Temperature, 66));

        Assert.Equal(66f, snapshot.CpuTemperature);
    }

    [Fact]
    public void ZeroAndMissingTemperaturesAreIgnored()
    {
        var snapshot = Snapshot(R(SensorHardware.Cpu, "CPU Package", SensorKind.Temperature, 0), R(SensorHardware.Cpu, "Core #1", SensorKind.Temperature, null));

        Assert.Null(snapshot.CpuTemperature);
    }

    [Fact]
    public void LoadPowerAndGpuReadingsComeFromTheRightSensors()
    {
        var snapshot = Snapshot(
            R(SensorHardware.Cpu, "CPU Total", SensorKind.Load, 23),
            R(SensorHardware.Cpu, "CPU Core #1", SensorKind.Load, 99),
            R(SensorHardware.Cpu, "CPU Package", SensorKind.Power, 41.5f),
            R(SensorHardware.Gpu, "GPU Core", SensorKind.Temperature, 67),
            R(SensorHardware.Gpu, "GPU Core", SensorKind.Load, 88),
            R(SensorHardware.Memory, "Memory", SensorKind.Load, 52));

        Assert.Equal(23f, snapshot.CpuLoad);
        Assert.Equal(41.5f, snapshot.CpuPower);
        Assert.Equal(67f, snapshot.GpuTemperature);
        Assert.Equal(88f, snapshot.GpuLoad);
        Assert.Equal(52f, snapshot.MemoryLoad);
    }

    [Fact]
    public void TheCpuClockAveragesTheCoresAndIgnoresIdleAndEffectiveReadings()
    {
        var snapshot = Snapshot(
            R(SensorHardware.Cpu, "Core #1", SensorKind.Clock, 4400),
            R(SensorHardware.Cpu, "Core #2", SensorKind.Clock, 4600),
            R(SensorHardware.Cpu, "Core #3", SensorKind.Clock, 0),
            R(SensorHardware.Cpu, "Core #1 (Effective)", SensorKind.Clock, 800),
            R(SensorHardware.Cpu, "Bus Speed", SensorKind.Clock, 100));

        Assert.Equal(4500f, snapshot.CpuClock);
    }

    [Fact]
    public void TheGpuPowerPrefersThePackageSensorAndFallsBackToTheFirstOne()
    {
        var withPackage = Snapshot(R(SensorHardware.Gpu, "GPU Core", SensorKind.Power, 80), R(SensorHardware.Gpu, "GPU Package", SensorKind.Power, 95));
        var withoutPackage = Snapshot(R(SensorHardware.Gpu, "GPU SoC", SensorKind.Power, 12), R(SensorHardware.Gpu, "GPU Memory", SensorKind.Power, 20));

        Assert.Equal(95f, withPackage.GpuPower);
        Assert.Equal(12f, withoutPackage.GpuPower);
        Assert.Null(Snapshot().GpuPower);
    }

    [Fact]
    public void TheGpuClockAndTheMemoryAmountsComeFromTheirSensors()
    {
        var snapshot = Snapshot(
            R(SensorHardware.Gpu, "GPU Core", SensorKind.Clock, 1850),
            R(SensorHardware.Gpu, "GPU Memory", SensorKind.Clock, 1750),
            R(SensorHardware.Memory, "Memory Used", SensorKind.Data, 11.2f),
            R(SensorHardware.Memory, "Memory Available", SensorKind.Data, 4.8f));

        Assert.Equal(1850f, snapshot.GpuClock);
        Assert.Equal(11.2f, snapshot.MemoryUsedGb);
        Assert.Equal(4.8f, snapshot.MemoryAvailableGb);
    }

    [Fact]
    public void AnEmptySnapshotHasNoReadings()
    {
        var snapshot = Snapshot();

        Assert.Null(snapshot.CpuTemperature);
        Assert.Null(snapshot.CpuLoad);
        Assert.Null(snapshot.GpuLoad);
    }
}
