using M1vClock.Core.Privacy;
using M1vClock.Core.Security;
using M1vClock.Hardware.Sensors;
using M1vClock.Hardware.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Hardware.Tests;

public sealed class StorageSensorMergeTests
{
    private static readonly FixtureWmiReader Wmi = FixtureWmiReader.Load("wmi-desktop-amd");
    private static readonly ElevationInfo Administrator = new(true, "S-1-5-21-1", "PC-EXEMPLO\\usuario");

    private static SensorReading Reading(string id, string hardware, string name, SensorKind kind, float value) =>
        new(id, hardware, SensorHardware.Storage, name, kind, value, value, value);

    private static SensorSnapshot RealSnapshot() => new(
        DateTimeOffset.UnixEpoch,
        [
            Reading("/nvme/0/temperature/0", "NX-512 2280", "Composite Temperature", SensorKind.Temperature, 41),
            Reading("/nvme/0/level/1", "NX-512 2280", "Life", SensorKind.Level, 50),
            Reading("/nvme/0/level/2", "NX-512 2280", "Percentage Used", SensorKind.Level, 50),
            Reading("/nvme/0/data/1", "NX-512 2280", "Data Read", SensorKind.Data, 62009),
            Reading("/nvme/0/data/2", "NX-512 2280", "Data Written", SensorKind.Data, 81565),
            Reading("/nvme/0/factor/0", "NX-512 2280", "Power On Count", SensorKind.Factor, 2184),
            Reading("/nvme/0/factor/1", "NX-512 2280", "Power On Hours", SensorKind.Factor, 8529),
            Reading("/hdd/0/factor/0", "ST1000VM002-1SD102", "Power On Hours", SensorKind.Factor, 7380),
        ]);

    private static IReadOnlyList<PhysicalDisk> RealDisks() =>
        new StorageCollector(Wmi, Administrator, new CollectingLogger<StorageCollector>()).ReadDisks().Disks;

    private static PhysicalDisk Disk(string model, StorageCounters? counters) =>
        new(1, model, new SensitiveText("SN"), "FW", DriveBus.Nvme, DriveMedia.Ssd, 512_000_000_000, WindowsHealth.Healthy, null, counters, ["C:"]);

    private static StorageCounters Counters(double? wear = null, long? hours = null, double? temperature = null) =>
        new(temperature, null, wear, hours, null, null, null, null, null);

    [Fact]
    public void TheRealNvmeThatWindowsReportsAsZeroWearShowsTheFiftyPercentTheDriveReports()
    {
        var disks = StorageSensorMerge.Apply(RealDisks(), RealSnapshot());

        var ssd = disks[1].Counters!;
        Assert.Equal(50, ssd.Wear);
        Assert.Equal(8529, ssd.PowerOnHours);
        Assert.Equal(2184, ssd.StartStopCycles);
        Assert.Equal(81_565_000_000_000UL, ssd.BytesWritten);
        Assert.Equal(62_009_000_000_000UL, ssd.BytesRead);
        Assert.Equal(41, ssd.Temperature);
        Assert.Equal(90, ssd.TemperatureMax);
    }

    [Fact]
    public void WhatWindowsAlreadyReportedIsNeverOverwrittenExceptWearWhichKeepsTheWorst()
    {
        var disks = StorageSensorMerge.Apply(RealDisks(), RealSnapshot());

        var hdd = disks[0].Counters!;
        Assert.Equal(7384, hdd.PowerOnHours);
        Assert.Equal(27, hdd.Temperature);
        Assert.Null(hdd.Wear);
    }

    [Fact]
    public void TheHigherOfTheTwoWearValuesWins()
    {
        var windowsHigher = StorageSensorMerge.Apply([Disk("NX-512 2280", Counters(wear: 60))], RealSnapshot());
        var smartHigher = StorageSensorMerge.Apply([Disk("NX-512 2280", Counters(wear: 10))], RealSnapshot());

        Assert.Equal(60, windowsHigher[0].Counters!.Wear);
        Assert.Equal(50, smartHigher[0].Counters!.Wear);
    }

    [Fact]
    public void WithoutAdministratorTheDiskGetsItsCountersFromTheSensorsAlone()
    {
        var merged = StorageSensorMerge.Apply([Disk("NX-512 2280", null)], RealSnapshot());

        var counters = merged[0].Counters!;
        Assert.Equal(41, counters.Temperature);
        Assert.Equal(50, counters.Wear);
        Assert.Equal(8529, counters.PowerOnHours);
    }

    [Fact]
    public void NoSnapshotOrNoMatchingSensorsLeavesTheDisksUntouched()
    {
        var disks = new[] { Disk("OUTRO SSD", Counters(wear: 3)) };

        Assert.Same(disks, StorageSensorMerge.Apply(disks, null));
        Assert.Equal(3, StorageSensorMerge.Apply(disks, RealSnapshot())[0].Counters!.Wear);
        Assert.Null(StorageSensorMerge.Apply([Disk("OUTRO SSD", null)], RealSnapshot())[0].Counters);
    }

    [Fact]
    public void TheModelIsMatchedWithoutCase()
    {
        var merged = StorageSensorMerge.Apply([Disk("nx-512 2280", null)], RealSnapshot());

        Assert.Equal(50, merged[0].Counters!.Wear);
    }

    [Fact]
    public void TwoDisksOfTheSameModelAreNotGuessedApart()
    {
        var twins = new[] { Disk("NX-512 2280", null), Disk("NX-512 2280", null) };

        var merged = StorageSensorMerge.Apply(twins, RealSnapshot());

        Assert.All(merged, d => Assert.Null(d.Counters));
    }

    [Fact]
    public void SensorsOfTwoDevicesWithTheSameNameAreNotMixed()
    {
        var snapshot = new SensorSnapshot(
            DateTimeOffset.UnixEpoch,
            [
                Reading("/nvme/0/level/2", "NX-512 2280", "Percentage Used", SensorKind.Level, 50),
                Reading("/nvme/1/level/2", "NX-512 2280", "Percentage Used", SensorKind.Level, 5),
            ]);

        Assert.Null(StorageSensorMerge.Apply([Disk("NX-512 2280", null)], snapshot)[0].Counters);
    }

    [Fact]
    public void ZeroOrMissingSensorValuesAreNotInventedAsData()
    {
        var snapshot = new SensorSnapshot(
            DateTimeOffset.UnixEpoch,
            [
                Reading("/nvme/0/data/2", "NX-512 2280", "Data Written", SensorKind.Data, 0),
                Reading("/nvme/0/temperature/0", "NX-512 2280", "Composite Temperature", SensorKind.Temperature, 0),
            ]);

        var counters = StorageSensorMerge.Apply([Disk("NX-512 2280", null)], snapshot)[0].Counters;

        Assert.Null(counters?.BytesWritten);
        Assert.Null(counters?.Temperature);
    }

    [Fact]
    public void ADiskWornToEightyFivePercentOnlyBySmartBecomesAWarning()
    {
        var snapshot = new SensorSnapshot(DateTimeOffset.UnixEpoch, [Reading("/nvme/0/level/2", "NX-512 2280", "Percentage Used", SensorKind.Level, 85)]);

        var merged = StorageSensorMerge.Apply([Disk("NX-512 2280", Counters(wear: 0))], snapshot)[0];

        Assert.Equal(DiskVerdict.Warning, DiskHealthEvaluator.Evaluate(merged).Verdict);
    }
}

public sealed class StorageServiceSensorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static SensorSnapshot Snapshot(DateTimeOffset at) => new(
        at,
        [new SensorReading("/nvme/0/level/2", "NX-512 2280", SensorHardware.Storage, "Percentage Used", SensorKind.Level, 50, 50, 50)]);

    private static StorageService Create(StubSensorService sensors, FakeTimeProvider clock)
    {
        var wmi = FixtureWmiReader.Load("wmi-desktop-amd");
        var collector = new StorageCollector(wmi, new ElevationInfo(true, "S-1-5-21-1", "PC-EXEMPLO\\usuario"), new CollectingLogger<StorageCollector>());
        return new StorageService(collector, sensors, clock, NullLogger<StorageService>.Instance);
    }

    private static async Task<int> WearOfTheNvmeAsync(StorageService service)
    {
        var report = await service.ReadAsync(TestContext.Current.CancellationToken);
        Assert.True(report.Disks.TryGet(out var disks));
        return (int)disks.Single(d => d.Disk.Model == "NX-512 2280").Disk.Counters!.Wear!.Value;
    }

    [Fact]
    public async Task AFreshSnapshotCompletesTheDiskDataAndTheLeaseIsReleased()
    {
        var clock = new FakeTimeProvider(Now);
        var sensors = new StubSensorService { State = SensorServiceState.Running, Latest = Snapshot(Now) };

        var wear = await WearOfTheNvmeAsync(Create(sensors, clock));

        Assert.Equal(50, wear);
        Assert.Equal(1, sensors.Subscriptions);
        Assert.Equal(1, sensors.Releases);
    }

    [Fact]
    public async Task APausedServiceStillOffersItsLastSnapshotEvenIfItIsOld()
    {
        var clock = new FakeTimeProvider(Now);
        var sensors = new StubSensorService { State = SensorServiceState.Paused, Latest = Snapshot(Now - TimeSpan.FromMinutes(10)) };

        Assert.Equal(50, await WearOfTheNvmeAsync(Create(sensors, clock)));
    }

    [Fact]
    public async Task AFailedSensorServiceDoesNotMakeTheStoragePageWait()
    {
        var clock = new FakeTimeProvider(Now);
        var sensors = new StubSensorService { State = SensorServiceState.Failed, Latest = null };
        var service = Create(sensors, clock);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Disks.TryGet(out var disks));
        Assert.Equal(0, disks.Single(d => d.Disk.Model == "NX-512 2280").Disk.Counters!.Wear);
    }

    [Fact]
    public async Task WhileTheSensorsAreStillStartingTheServiceWaitsForTheFirstSnapshot()
    {
        var clock = new FakeTimeProvider(Now);
        var sensors = new StubSensorService { State = SensorServiceState.Starting, Latest = null };
        var task = WearOfTheNvmeAsync(Create(sensors, clock));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(task.IsCompleted);

        sensors.State = SensorServiceState.Running;
        sensors.Latest = Snapshot(clock.GetUtcNow());
        clock.Advance(TimeSpan.FromMilliseconds(250));

        Assert.Equal(50, await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IfNoSnapshotEverComesTheServiceGivesUpAfterTheLimitAndKeepsWindowsData()
    {
        var clock = new FakeTimeProvider(Now);
        var sensors = new StubSensorService { State = SensorServiceState.Starting, Latest = null };
        var task = WearOfTheNvmeAsync(Create(sensors, clock));

        for (var i = 0; i < 40 && !task.IsCompleted; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }
}
