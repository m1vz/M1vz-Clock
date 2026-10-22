using System.IO;
using M1vClock.App.Hosting;
using M1vClock.App.Services;
using M1vClock.Benchmarks;
using M1vClock.Benchmarks.Io;
using M1vClock.Benchmarks.Results;
using M1vClock.Benchmarks.Safety;
using M1vClock.Core.Paths;
using M1vClock.Core.Results;
using M1vClock.Core.Security;
using M1vClock.Hardware.Gpu;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Ram;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.App.Tests;

public sealed class HeavyTestGateTests
{
    private readonly FakeSensorService _sensors = new() { State = SensorServiceState.Running };
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly CollectingLogger<HeavyTestGate> _logger = new();

    private HeavyTestGate Create() => new(_sensors, _clock, _logger);

    [Fact]
    public async Task EnteringTakesTheSensorsAndPausesTheGeneralReading()
    {
        var gate = Create();

        using var lease = await gate.EnterAsync("cpu", TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(gate.IsBusy);
        Assert.Equal("cpu", gate.CurrentName);
        Assert.Equal(1, _sensors.Subscriptions);
        Assert.Equal(1, _sensors.Pauses);
        Assert.Equal(0, _sensors.PauseReleases);
    }

    [Fact]
    public async Task LeavingGivesEverythingBackExactlyOnce()
    {
        var gate = Create();
        var lease = await gate.EnterAsync("cpu", TestContext.Current.CancellationToken);

        lease!.Dispose();
        lease.Dispose();

        Assert.False(gate.IsBusy);
        Assert.Null(gate.CurrentName);
        Assert.Equal(1, _sensors.PauseReleases);
        Assert.Equal(1, _sensors.Releases);
    }

    [Fact]
    public async Task ASecondTestCannotEnterWhileOneIsRunningAndCanAfterwards()
    {
        var gate = Create();
        var first = await gate.EnterAsync("cpu", TestContext.Current.CancellationToken);

        var refused = await gate.EnterAsync("disk", TestContext.Current.CancellationToken);
        first!.Dispose();
        using var second = await gate.EnterAsync("disk", TestContext.Current.CancellationToken);

        Assert.Null(refused);
        Assert.NotNull(second);
        Assert.Equal(2, _sensors.Subscriptions);
    }

    [Fact]
    public async Task TheChangeEventFiresWhenATestStartsAndWhenItEnds()
    {
        var gate = Create();
        var states = new List<bool>();
        gate.Changed += (_, _) => states.Add(gate.IsBusy);

        var lease = await gate.EnterAsync("cpu", TestContext.Current.CancellationToken);
        lease!.Dispose();

        Assert.Equal([true, false], states);
    }

    [Fact]
    public async Task WhileTheSensorsAreStillOpeningTheGateWaitsForThem()
    {
        _sensors.State = SensorServiceState.Starting;
        var gate = Create();

        var entering = gate.EnterAsync("cpu", TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(entering.IsCompleted);
        Assert.Equal(0, _sensors.Pauses);

        _sensors.Change(SensorServiceState.Running);
        using var lease = await entering.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.Equal(1, _sensors.Pauses);
    }

    [Fact]
    public async Task AFailedSensorServiceDoesNotBlockTheTest()
    {
        _sensors.State = SensorServiceState.Failed;
        var gate = Create();

        using var lease = await gate.EnterAsync("cpu", TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
    }

    [Fact]
    public async Task SensorsThatNeverOpenDoNotBlockTheTestForever()
    {
        _sensors.State = SensorServiceState.Starting;
        var gate = Create();

        var entering = gate.EnterAsync("cpu", TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _clock.Advance(HeavyTestGate.SensorWait + TimeSpan.FromSeconds(1));
        using var lease = await entering.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 2022);
    }

    [Fact]
    public async Task CancellingWhileWaitingLeavesNoLeaseAndFreesTheGate()
    {
        _sensors.State = SensorServiceState.Starting;
        var gate = Create();
        using var cancellation = new CancellationTokenSource();

        var entering = gate.EnterAsync("cpu", cancellation.Token);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => entering);
        Assert.False(gate.IsBusy);
        Assert.Equal(1, _sensors.Releases);
        Assert.Equal(0, _sensors.Pauses);
    }

    [Fact]
    public void AStopRequestReachesWhoeverIsListening()
    {
        var gate = Create();
        var count = 0;
        gate.StopRequested += (_, _) => count++;

        gate.RequestStop();

        Assert.Equal(1, count);
    }
}

public sealed class SensorComponentProbeTests
{
    private static SensorReading Reading(SensorHardware hardware, string name, SensorKind kind, float value) =>
        new($"/{hardware}/{name}", "x", hardware, name, kind, value, value, value);

    [Fact]
    public void TheCpuSampleCarriesTemperatureLoadPowerAndClock()
    {
        var sensors = new FakeSensorService
        {
            Component = new SensorSnapshot(
                DateTimeOffset.UnixEpoch,
                [
                    Reading(SensorHardware.Cpu, "CPU Package", SensorKind.Temperature, 63),
                    Reading(SensorHardware.Cpu, "CPU Total", SensorKind.Load, 87),
                    Reading(SensorHardware.Cpu, "Package", SensorKind.Power, 91),
                    Reading(SensorHardware.Cpu, "Core #1", SensorKind.Clock, 4400),
                ]),
        };

        var sample = new SensorComponentProbe(sensors).Read(ThermalComponent.Cpu);

        Assert.Equal(new ComponentSample(63, 87, 91, 4400), sample);
    }

    [Fact]
    public void TheGpuSampleUsesTheGpuCoreSensors()
    {
        var sensors = new FakeSensorService
        {
            Component = new SensorSnapshot(
                DateTimeOffset.UnixEpoch,
                [
                    Reading(SensorHardware.Gpu, "GPU Core", SensorKind.Temperature, 71),
                    Reading(SensorHardware.Gpu, "GPU Core", SensorKind.Load, 99),
                    Reading(SensorHardware.Gpu, "GPU Core", SensorKind.Clock, 1850),
                ]),
        };

        var sample = new SensorComponentProbe(sensors).Read(ThermalComponent.Gpu);

        Assert.Equal(71, sample?.Temperature);
        Assert.Equal(99, sample?.Load);
        Assert.Equal(1850, sample?.Clock);
        Assert.Null(sample?.Power);
    }

    [Fact]
    public void WithoutASnapshotThereIsNoSample()
    {
        Assert.Null(new SensorComponentProbe(new FakeSensorService()).Read(ThermalComponent.Cpu));
    }
}

public sealed class MachineProfileProviderTests
{
    private static readonly AppInfo Info = new("0.1.0", "C:\\dados", true);

    private static MachineProfileProvider Create() =>
        new(new StubPlatformService(), new StubGpuService(), new StubRamService(), Info, NullLogger<MachineProfileProvider>.Instance);

    private static PlatformReport Platform() => new(
        ReadResult.Success(new OsInfo("Windows 11 Home", "10.0", "26200", "25H2", "9711", "x64", "PC-EXEMPLO")),
        ReadResult.Failure<BoardInfo>("n/d"),
        ReadResult.Failure<BiosInfo>("n/d"),
        ReadResult.Success<IReadOnlyList<CpuInfo>>([new CpuInfo(" AMD Ryzen 7 5700X 8-Core Processor ", "AMD", "AM4", 8, 16, 3400, 4096, 32768, true)]));

    private static GpuInfo Gpu(string name, ulong memory, bool integrated) => new(
        name,
        "AMD",
        null,
        integrated ? new GpuSpec("Vega", "GCN", "7 nm", 0, 0, 512, 8, 32, "Memória do sistema (compartilhada)", 400, 2000, 15, "2020", null) : null,
        memory,
        0,
        "31.0",
        null,
        DriverSignature.Whql,
        null,
        null,
        [],
        null);

    [Fact]
    public void TheProfileNamesTheCpuTheDedicatedGpuTheMemoryAndTheSystemWithoutTheComputerName()
    {
        var gpus = new GpuReport(ReadResult.Success<IReadOnlyList<GpuInfo>>([Gpu("AMD Radeon(TM) Graphics", 512UL << 20, true), Gpu("AMD Radeon RX 5500 XT", 8UL << 30, false)]));
        var ram = new RamReport(ReadResult.Failure<RamInventory>("n/d"), ReadResult.Success(new RamUsage(34_300_000_000, 4_000_000_000, 0, 0)));

        var machine = Create().Build(Platform(), gpus, ram);

        Assert.Equal("AMD Ryzen 7 5700X 8-Core Processor", machine.Cpu);
        Assert.Equal("AMD Radeon RX 5500 XT", machine.Gpu);
        Assert.Equal(16, machine.LogicalProcessors);
        Assert.Equal(31.9, machine.MemoryGigabytes);
        Assert.Equal("Windows 11 Home 26200.9711", machine.OsVersion);
        Assert.Equal("0.1.0", machine.AppVersion);
        Assert.DoesNotContain("PC-EXEMPLO", machine.OsVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void WhateverCouldNotBeReadStaysEmptyInsteadOfInvented()
    {
        var machine = Create().Build(null, null, null);

        Assert.Equal(string.Empty, machine.Cpu);
        Assert.Equal(string.Empty, machine.Gpu);
        Assert.Equal(string.Empty, machine.OsVersion);
        Assert.Equal(0, machine.MemoryGigabytes);
        Assert.Equal(Environment.ProcessorCount, machine.LogicalProcessors);
    }

    [Fact]
    public async Task TheProfileIsReadOnceAndKept()
    {
        var platform = new StubPlatformService();
        var provider = new MachineProfileProvider(platform, new StubGpuService(), new StubRamService(), Info, NullLogger<MachineProfileProvider>.Instance);

        var first = await provider.GetAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, platform.Reads);
    }

    private sealed class StubPlatformService : IPlatformService
    {
        public int Reads { get; private set; }

        public Task<PlatformReport> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Platform());
        }
    }

    private sealed class StubGpuService : IGpuService
    {
        public Task<GpuReport> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new GpuReport(ReadResult.Success<IReadOnlyList<GpuInfo>>([Gpu("AMD Radeon RX 5500 XT", 8UL << 30, false)])));
    }

    private sealed class StubRamService : IRamService
    {
        public Task<RamReport> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RamReport(ReadResult.Failure<RamInventory>("n/d"), ReadResult.Success(new RamUsage(34_300_000_000, 4_000_000_000, 0, 0))));
    }
}

public sealed class BenchmarkWorkLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "m1vclock-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private BenchmarkWorkLocation Create(bool elevated) =>
        new(new AppPaths(_root, isHardened: false), new ElevationInfo(elevated, "S-1-5-21-1", "PC\\usuario"));

    [Fact]
    public void AnElevatedRunUsesAHardenedFolderAtTheRootOfTheTestedDrive()
    {
        var place = Create(elevated: true).For("D:\\");

        Assert.Equal("D:\\", place.Parent);
        Assert.True(place.Hardened);
        Assert.Equal("D:", place.Drive);
    }

    [Fact]
    public void WithoutAdministratorTheSystemDriveUsesTheAppsOwnTempFolder()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)!;

        var place = Create(elevated: false).For(systemRoot);

        Assert.Equal(Path.Combine(_root, "temp"), place.Parent);
        Assert.False(place.Hardened);
        Assert.True(Directory.Exists(place.Parent));
    }

    [Fact]
    public void WithoutAdministratorAnotherDriveUsesItsRoot()
    {
        var place = Create(elevated: false).For("Z:\\");

        Assert.Equal("Z:\\", place.Parent);
        Assert.False(place.Hardened);
    }

    [Fact]
    public void ADeepPathIsReducedToItsDrive()
    {
        Assert.Equal("D:", Create(elevated: true).For("D:\\Jogos\\Pasta").Drive);
    }

    [Fact]
    public void TheSweepCoversTheAppsTempFolderAndTheFixedDrives()
    {
        var parents = Create(elevated: false).SweepParents();

        Assert.Contains(Path.Combine(_root, "temp"), parents);
        Assert.Contains(parents, p => p.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class BenchmarkTempSweeperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "m1vclock-tests", Guid.NewGuid().ToString("N"));

    public BenchmarkTempSweeperTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "temp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ALeftoverFromACrashedRunIsRemovedFromTheAppTempFolder()
    {
        var leftover = Path.Combine(_root, "temp", BenchTempArea.Prefix + "0123456789abcdef");
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "bench.dat"), "x");
        Directory.SetCreationTimeUtc(leftover, DateTime.UtcNow.AddHours(-1));
        var location = new BenchmarkWorkLocation(new AppPaths(_root, isHardened: false), new ElevationInfo(false, "S-1-5-21-1", "PC\\usuario"));
        var sweeper = new BenchmarkTempSweeper(location, NullLogger<BenchmarkTempSweeper>.Instance);

        sweeper.Sweep(TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(leftover));
    }
}

public sealed class BenchmarkHistoryRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "m1vclock-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServiceProvider Start() => new ServiceCollection()
        .AddSingleton(new AppPaths(_root, isHardened: false))
        .AddSingleton(new ElevationInfo(false, "S-1-5-21-1", "PC-EXEMPLO\\usuario"))
        .AddSingleton(TimeProvider.System)
        .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
        .AddBenchmarksModule()
        .BuildServiceProvider();

    private static BenchmarkRecord Record(double score) => new()
    {
        Id = Guid.NewGuid(),
        At = DateTimeOffset.UtcNow.AddMinutes(-1),
        Test = BenchmarkTests.CpuMulti,
        ScoreVersion = 1,
        Score = score,
        Metrics = new Dictionary<string, double> { ["seconds"] = 14 },
        Machine = StubMachine.Profile,
    };

    [Fact]
    public void ReopeningTheAppKeepsEveryResultAlreadySavedAndTheNextOneIsAddedToThem()
    {
        using (var first = Start())
        {
            var history = first.GetRequiredService<BenchmarkHistory>();
            history.Add(Record(3000));
            history.Add(Record(3100));
        }

        using var second = Start();
        var reopened = second.GetRequiredService<BenchmarkHistory>();
        Assert.Equal([3100, 3000], reopened.Records.Select(r => r.Score));

        reopened.Add(Record(3200));

        using var third = Start();
        Assert.Equal(3, third.GetRequiredService<BenchmarkHistory>().Records.Count);
    }

    [Fact]
    public void TheHistoryLivesInTheFolderOfTheCurrentUserAndNotInASharedOne()
    {
        using var provider = Start();
        provider.GetRequiredService<BenchmarkHistory>().Add(Record(3000));

        var file = Directory.EnumerateFiles(_root, BenchmarksModule.HistoryFileName, SearchOption.AllDirectories).Single();

        Assert.Contains("S-1-5-21-1", file, StringComparison.Ordinal);
    }
}
