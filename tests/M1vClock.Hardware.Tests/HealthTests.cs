using M1vClock.Core.Privacy;
using M1vClock.Core.Results;
using M1vClock.Hardware.Devices;
using M1vClock.Hardware.Health;
using M1vClock.Hardware.Power;
using M1vClock.Hardware.Sensors;
using M1vClock.Hardware.Storage;

namespace M1vClock.Hardware.Tests;

public sealed class HealthEvaluatorTests
{
    private static HealthInputs Healthy() => new(
        CpuSensorProblem.None,
        [DiskVerdict.Good],
        40,
        60,
        new PowerPlan(PowerPlanParser.HighPerformance, "Alto desempenho", PowerPlanKind.HighPerformance),
        PowerSource.DesktopMains,
        null,
        0);

    private static HealthCheck Check(HealthReport report, HealthKind kind) => Assert.Single(report.Checks, c => c.Kind == kind);

    [Fact]
    public void AHealthyPcHasNoProblems()
    {
        var report = HealthEvaluator.Evaluate(Healthy());

        Assert.Equal(HealthStatus.Ok, report.Overall);
        Assert.Equal(0, report.Problems);
        Assert.All(report.Checks, c => Assert.Equal(HealthStatus.Ok, c.Status));
        Assert.Equal([HealthKind.CpuSensors, HealthKind.Disks, HealthKind.Memory, HealthKind.SystemDrive, HealthKind.PowerPlan, HealthKind.Usb], report.Checks.Select(c => c.Kind));
    }

    [Fact]
    public void MissingInputsSimplyDropTheirChecks()
    {
        var report = HealthEvaluator.Evaluate(new HealthInputs(null, null, null, null, null, null, null, null));

        Assert.Empty(report.Checks);
        Assert.Equal(HealthStatus.Unknown, report.Overall);
    }

    [Theory]
    [InlineData(CpuSensorProblem.None, HealthStatus.Ok)]
    [InlineData(CpuSensorProblem.NotElevated, HealthStatus.Attention)]
    [InlineData(CpuSensorProblem.PawnIoMissing, HealthStatus.Attention)]
    [InlineData(CpuSensorProblem.ReadFailed, HealthStatus.Attention)]
    public void CpuSensorsAreAttentionWheneverTheTemperatureIsMissing(CpuSensorProblem problem, HealthStatus expected)
    {
        var check = Check(HealthEvaluator.Evaluate(Healthy() with { CpuSensors = problem }), HealthKind.CpuSensors);

        Assert.Equal(expected, check.Status);
        Assert.Equal((int)problem, check.Value);
    }

    [Fact]
    public void TheWorstDiskDecidesAndCountsTheProblemDisks()
    {
        var report = HealthEvaluator.Evaluate(Healthy() with { Disks = [DiskVerdict.Good, DiskVerdict.Warning, DiskVerdict.Critical, DiskVerdict.Warning] });

        var check = Check(report, HealthKind.Disks);
        Assert.Equal(HealthStatus.Critical, check.Status);
        Assert.Equal(3, check.Value);
        Assert.Equal(HealthStatus.Critical, report.Overall);
    }

    [Fact]
    public void DisksThatCouldNotBeJudgedAreUnknownNotOk()
    {
        var check = Check(HealthEvaluator.Evaluate(Healthy() with { Disks = [DiskVerdict.Unknown, DiskVerdict.Unknown] }), HealthKind.Disks);

        Assert.Equal(HealthStatus.Unknown, check.Status);
    }

    [Fact]
    public void AGoodDiskNextToAnUnknownOneIsStillOk()
    {
        Assert.Equal(HealthStatus.Ok, Check(HealthEvaluator.Evaluate(Healthy() with { Disks = [DiskVerdict.Good, DiskVerdict.Unknown] }), HealthKind.Disks).Status);
    }

    [Fact]
    public void NoDisksMeansNoDiskCheck()
    {
        Assert.DoesNotContain(HealthEvaluator.Evaluate(Healthy() with { Disks = [] }).Checks, c => c.Kind == HealthKind.Disks);
    }

    [Theory]
    [InlineData(40, HealthStatus.Ok)]
    [InlineData(84.9, HealthStatus.Ok)]
    [InlineData(85, HealthStatus.Attention)]
    [InlineData(94.9, HealthStatus.Attention)]
    [InlineData(95, HealthStatus.Critical)]
    public void MemoryPressureHasTwoThresholds(double used, HealthStatus expected)
    {
        Assert.Equal(expected, Check(HealthEvaluator.Evaluate(Healthy() with { MemoryUsedPercent = used }), HealthKind.Memory).Status);
    }

    [Theory]
    [InlineData(60, HealthStatus.Ok)]
    [InlineData(10, HealthStatus.Ok)]
    [InlineData(9.9, HealthStatus.Attention)]
    [InlineData(5, HealthStatus.Attention)]
    [InlineData(4.9, HealthStatus.Critical)]
    public void SystemDriveSpaceHasTwoThresholds(double free, HealthStatus expected)
    {
        Assert.Equal(expected, Check(HealthEvaluator.Evaluate(Healthy() with { SystemDriveFreePercent = free }), HealthKind.SystemDrive).Status);
    }

    [Theory]
    [InlineData(PowerPlanKind.Balanced, PowerSource.Mains, HealthStatus.Attention)]
    [InlineData(PowerPlanKind.PowerSaver, PowerSource.DesktopMains, HealthStatus.Attention)]
    [InlineData(PowerPlanKind.HighPerformance, PowerSource.Mains, HealthStatus.Ok)]
    [InlineData(PowerPlanKind.Custom, PowerSource.DesktopMains, HealthStatus.Ok)]
    public void OnlyAKnownSlowPlanOnTheMainsIsAnAttention(PowerPlanKind kind, PowerSource source, HealthStatus expected)
    {
        var plan = new PowerPlan(Guid.NewGuid(), "Plano", kind);

        Assert.Equal(expected, Check(HealthEvaluator.Evaluate(Healthy() with { Plan = plan, PowerSource = source }), HealthKind.PowerPlan).Status);
    }

    [Fact]
    public void TheEnergyPlanIsNotJudgedOnBattery()
    {
        var plan = new PowerPlan(PowerPlanParser.Balanced, "Equilibrado", PowerPlanKind.Balanced);

        Assert.DoesNotContain(HealthEvaluator.Evaluate(Healthy() with { Plan = plan, PowerSource = PowerSource.Battery }).Checks, c => c.Kind == HealthKind.PowerPlan);
    }

    [Theory]
    [InlineData(BatteryVerdict.Good, HealthStatus.Ok)]
    [InlineData(BatteryVerdict.Warning, HealthStatus.Attention)]
    [InlineData(BatteryVerdict.Worn, HealthStatus.Critical)]
    [InlineData(BatteryVerdict.Unknown, HealthStatus.Unknown)]
    public void TheBatteryVerdictBecomesAHealthStatus(BatteryVerdict verdict, HealthStatus expected)
    {
        Assert.Equal(expected, Check(HealthEvaluator.Evaluate(Healthy() with { Batteries = [verdict] }), HealthKind.Battery).Status);
    }

    [Fact]
    public void ADesktopWithoutBatteriesHasNoBatteryCheck()
    {
        Assert.DoesNotContain(HealthEvaluator.Evaluate(Healthy() with { Batteries = [] }).Checks, c => c.Kind == HealthKind.Battery);
    }

    [Fact]
    public void UsbProblemsAreAttentionAndCounted()
    {
        var check = Check(HealthEvaluator.Evaluate(Healthy() with { UsbProblems = 2 }), HealthKind.Usb);

        Assert.Equal(HealthStatus.Attention, check.Status);
        Assert.Equal(2, check.Value);
    }

    [Fact]
    public void AnAttentionWithoutCriticalsIsAnAttentionOverall()
    {
        var report = HealthEvaluator.Evaluate(Healthy() with { UsbProblems = 1, MemoryUsedPercent = 90 });

        Assert.Equal(HealthStatus.Attention, report.Overall);
        Assert.Equal(2, report.Problems);
    }

    [Fact]
    public void UnknownChecksNeverMakeTheOverallWorse()
    {
        var report = HealthEvaluator.Evaluate(Healthy() with { Disks = [DiskVerdict.Unknown] });

        Assert.Equal(HealthStatus.Ok, report.Overall);
        Assert.Equal(0, report.Problems);
    }
}

public sealed class HealthServiceTests
{
    private static PhysicalDisk Disk() =>
        new(1, "NX-512", new SensitiveText("SN"), "FW", DriveBus.Nvme, DriveMedia.Ssd, 512_000_000_000, WindowsHealth.Healthy, null, null, ["C:"]);

    private static Volume Drive(string letter, ulong total, ulong free) => new(letter, "Sistema", VolumeKind.Internal, "NTFS", "NX-512", total, free);

    private static StorageReport Storage(DiskVerdict verdict, params Volume[] volumes) => new(
        ReadResult.Success<IReadOnlyList<DiskReport>>([new DiskReport(Disk(), new DiskAssessment(verdict, []))]),
        ReadResult.Success<IReadOnlyList<Volume>>(volumes),
        CounterAccess.Read,
        CounterAccess.Read);

    private static PowerReport Power(PowerPlanKind kind = PowerPlanKind.Custom) => new(
        ReadResult.Success<IReadOnlyList<BatteryReport>>([]),
        PowerSource.DesktopMains,
        ReadResult.Success(new PowerPlan(Guid.NewGuid(), "Plano", kind)));

    private static SensorSnapshot Snapshot(float? cpuTemperature, float memoryLoad) => new(
        DateTimeOffset.UnixEpoch,
        [
            FakeSensorSource.Reading("/ram/load/0", SensorHardware.Memory, "Memory", SensorKind.Load, memoryLoad),
            FakeSensorSource.Reading("cpu", SensorHardware.Cpu, "CPU Package", SensorKind.Temperature, cpuTemperature),
        ]);

    private sealed class StorageStub(StorageReport report) : IStorageService
    {
        public Task<StorageReport> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(report);
    }

    private sealed class PowerStub(PowerReport report) : IPowerService
    {
        public Task<PowerReport> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(report);
    }

    private sealed class PawnStub(bool installed) : IPawnIoProbe
    {
        public bool IsInstalled { get; } = installed;
    }

    private static HealthService Create(StorageReport storage, PowerReport power, FakeUsbService usb, bool pawnIo = true) =>
        new(new StorageStub(storage), new PowerStub(power), usb, new PawnStub(pawnIo), "C:");

    [Fact]
    public async Task EverySourceFeedsItsCheck()
    {
        var usb = new FakeUsbService();
        usb.Set(new UsbDeviceInfo("USB\\VID_1&PID_2\\A", "Placa", "Ports", UsbCategory.SerialPort, "0001", "0002", null, null, 28));
        var service = Create(Storage(DiskVerdict.Warning, Drive("C:", 1000, 80)), Power(PowerPlanKind.Balanced), usb);

        var report = await service.ReadAsync(Snapshot(60f, 90f), elevated: true, TestContext.Current.CancellationToken);

        Assert.Equal(
            [HealthStatus.Ok, HealthStatus.Attention, HealthStatus.Attention, HealthStatus.Attention, HealthStatus.Attention, HealthStatus.Attention],
            report.Checks.Select(c => c.Status));
        Assert.Equal(8, Assert.Single(report.Checks, c => c.Kind == HealthKind.SystemDrive).Value);
        Assert.Equal(1, Assert.Single(report.Checks, c => c.Kind == HealthKind.Usb).Value);
    }

    [Fact]
    public async Task OnlyTheSystemDriveCountsForTheSpaceCheck()
    {
        var service = Create(Storage(DiskVerdict.Good, Drive("D:", 1000, 10), Drive("C:", 1000, 500)), Power(), new FakeUsbService());

        var report = await service.ReadAsync(null, elevated: false, TestContext.Current.CancellationToken);

        Assert.Equal(50, Assert.Single(report.Checks, c => c.Kind == HealthKind.SystemDrive).Value);
    }

    [Fact]
    public async Task TheReportCarriesTheSystemVolumeForTheOverview()
    {
        var service = Create(Storage(DiskVerdict.Good, Drive("D:", 1000, 10), Drive("C:", 2000, 500)), Power(), new FakeUsbService());

        var report = await service.ReadAsync(null, elevated: false, TestContext.Current.CancellationToken);

        Assert.Equal("C:", report.SystemVolume!.Letter);
        Assert.Equal(2000UL, report.SystemVolume.TotalBytes);
    }

    [Fact]
    public async Task WithoutTheSystemVolumeTheReportHasNone()
    {
        var service = Create(Storage(DiskVerdict.Good, Drive("D:", 1000, 10)), Power(), new FakeUsbService());

        var report = await service.ReadAsync(null, elevated: false, TestContext.Current.CancellationToken);

        Assert.Null(report.SystemVolume);
    }

    [Fact]
    public async Task WithoutASnapshotTheSensorAndMemoryChecksAreLeftOut()
    {
        var service = Create(Storage(DiskVerdict.Good, Drive("C:", 1000, 500)), Power(), new FakeUsbService());

        var report = await service.ReadAsync(null, elevated: true, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(report.Checks, c => c.Kind is HealthKind.CpuSensors or HealthKind.Memory);
    }

    [Fact]
    public async Task ACpuWithoutTemperatureIsDiagnosedWithTheCurrentElevationAndDriver()
    {
        var service = Create(Storage(DiskVerdict.Good, Drive("C:", 1000, 500)), Power(), new FakeUsbService(), pawnIo: false);

        var report = await service.ReadAsync(Snapshot(null, 40f), elevated: true, TestContext.Current.CancellationToken);

        var check = Assert.Single(report.Checks, c => c.Kind == HealthKind.CpuSensors);
        Assert.Equal(HealthStatus.Attention, check.Status);
        Assert.Equal((int)CpuSensorProblem.PawnIoMissing, check.Value);
    }

    [Fact]
    public async Task AFailedStorageReadDropsTheDiskAndSpaceChecksInsteadOfFailing()
    {
        var failed = new StorageReport(
            ReadResult.Failure<IReadOnlyList<DiskReport>>("Sem WMI."),
            ReadResult.Failure<IReadOnlyList<Volume>>("Sem WMI."),
            CounterAccess.Unavailable,
            CounterAccess.Unavailable);
        var service = Create(failed, Power(), new FakeUsbService());

        var report = await service.ReadAsync(Snapshot(60f, 40f), elevated: true, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(report.Checks, c => c.Kind is HealthKind.Disks or HealthKind.SystemDrive);
        Assert.Contains(report.Checks, c => c.Kind == HealthKind.Memory);
    }

    [Fact]
    public async Task ThisMachinesRealSourcesProduceAReport()
    {
        var wmi = new Wmi.WindowsWmiReader();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<StorageService>.Instance;
        var service = new HealthService(
            new StorageService(new StorageCollector(wmi, Microsoft.Extensions.Logging.Abstractions.NullLogger<StorageCollector>.Instance), logger),
            new PowerService(new PowerCollector(wmi, Microsoft.Extensions.Logging.Abstractions.NullLogger<PowerCollector>.Instance), new Core.Processes.ProcessRunner(Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Processes.ProcessRunner>.Instance), Microsoft.Extensions.Logging.Abstractions.NullLogger<PowerService>.Instance),
            new UsbDeviceService(new UsbCollector(wmi), Microsoft.Extensions.Logging.Abstractions.NullLogger<UsbDeviceService>.Instance),
            new RegistryPawnIoProbe(new Core.RegistryValues.WindowsRegistryAccess()));

        var report = await service.ReadAsync(null, elevated: false, TestContext.Current.CancellationToken);

        Assert.NotEmpty(report.Checks);
        Assert.Contains(report.Checks, c => c.Kind == HealthKind.Usb);
    }
}
