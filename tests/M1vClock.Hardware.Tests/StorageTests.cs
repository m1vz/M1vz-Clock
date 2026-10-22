using System.Management;
using M1vClock.Core.Privacy;
using M1vClock.Core.Security;
using M1vClock.Hardware.Storage;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class WmiReferenceTests
{
    [Theory]
    [InlineData("\\\\DESKTOP\\root\\cimv2:Win32_DiskDrive.DeviceID=\"\\\\\\\\.\\\\PHYSICALDRIVE0\"", "\\\\.\\PHYSICALDRIVE0")]
    [InlineData("\\\\DESKTOP\\root\\cimv2:Win32_DiskPartition.DeviceID=\"Disk #1, Partition #1\"", "Disk #1, Partition #1")]
    [InlineData("\\\\DESKTOP\\root\\cimv2:Win32_LogicalDisk.DeviceID=\"C:\"", "C:")]
    [InlineData("sem referência", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractsTheDeviceIdFromAReference(string? reference, string? expected)
    {
        Assert.Equal(expected, WmiReference.DeviceId(reference));
    }
}

public sealed class DiskHealthEvaluatorTests
{
    private static PhysicalDisk Disk(
        DriveMedia media = DriveMedia.Hdd,
        DriveBus bus = DriveBus.Sata,
        WindowsHealth health = WindowsHealth.Healthy,
        bool? predicted = null,
        StorageCounters? counters = null) =>
        new(0, "Disco", new SensitiveText("S"), "FW", bus, media, 1_000_000_000_000, health, predicted, counters, ["C:"]);

    private static StorageCounters Counters(double? temperature = null, double? wear = null, long? readUncorrected = null, long? writeUncorrected = null, long? hours = null) =>
        new(temperature, null, wear, hours, null, null, readUncorrected, writeUncorrected, null);

    [Fact]
    public void AHealthyDiskWithDataIsGood()
    {
        var result = DiskHealthEvaluator.Evaluate(Disk());

        Assert.Equal(DiskVerdict.Good, result.Verdict);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ADiskThatReportsNothingIsUnknown()
    {
        var result = DiskHealthEvaluator.Evaluate(Disk(health: WindowsHealth.Unknown));

        Assert.Equal(DiskVerdict.Unknown, result.Verdict);
    }

    [Fact]
    public void AUsbDriveWithoutDataIsUnknown()
    {
        var result = DiskHealthEvaluator.Evaluate(Disk(media: DriveMedia.Unknown, bus: DriveBus.Usb, health: WindowsHealth.Unknown));

        Assert.Equal(DiskVerdict.Unknown, result.Verdict);
    }

    [Fact]
    public void ACounterAloneIsEnoughDataToCallTheDiskGood()
    {
        var result = DiskHealthEvaluator.Evaluate(Disk(health: WindowsHealth.Unknown, counters: Counters(temperature: 35)));

        Assert.Equal(DiskVerdict.Good, result.Verdict);
    }

    [Fact]
    public void APredictedFailureIsCritical()
    {
        var result = DiskHealthEvaluator.Evaluate(Disk(predicted: true));

        Assert.Equal(DiskVerdict.Critical, result.Verdict);
        Assert.Equal(FindingKind.FailurePredicted, Assert.Single(result.Findings).Kind);
    }

    [Fact]
    public void NoPredictedFailureIsFineAndMissingIsNotAProblem()
    {
        Assert.Equal(DiskVerdict.Good, DiskHealthEvaluator.Evaluate(Disk(predicted: false)).Verdict);
        Assert.Equal(DiskVerdict.Good, DiskHealthEvaluator.Evaluate(Disk(predicted: null)).Verdict);
    }

    [Theory]
    [InlineData(WindowsHealth.Unhealthy, DiskVerdict.Critical, FindingKind.WindowsUnhealthy)]
    [InlineData(WindowsHealth.Warning, DiskVerdict.Warning, FindingKind.WindowsWarning)]
    public void WindowsHealthMapsToAVerdict(WindowsHealth health, DiskVerdict verdict, FindingKind kind)
    {
        var result = DiskHealthEvaluator.Evaluate(Disk(health: health));

        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(kind, Assert.Single(result.Findings).Kind);
    }

    [Theory]
    [InlineData(79.9, DiskVerdict.Good, null)]
    [InlineData(80.0, DiskVerdict.Warning, FindingKind.WearHigh)]
    [InlineData(94.9, DiskVerdict.Warning, FindingKind.WearHigh)]
    [InlineData(95.0, DiskVerdict.Critical, FindingKind.WearEndOfLife)]
    [InlineData(100.0, DiskVerdict.Critical, FindingKind.WearEndOfLife)]
    public void WearThresholdsAreEightyAndNinetyFivePercent(double wear, DiskVerdict verdict, FindingKind? kind)
    {
        var result = DiskHealthEvaluator.Evaluate(Disk(DriveMedia.Ssd, DriveBus.Nvme, counters: Counters(wear: wear)));

        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(kind, result.Findings.Count == 0 ? null : result.Findings[0].Kind);
        if (kind is not null)
        {
            Assert.Equal(wear, result.Findings[0].Value);
        }
    }

    [Theory]
    [InlineData(DriveMedia.Ssd, DriveBus.Nvme, 69.9, DiskVerdict.Good)]
    [InlineData(DriveMedia.Ssd, DriveBus.Nvme, 70.0, DiskVerdict.Warning)]
    [InlineData(DriveMedia.Ssd, DriveBus.Nvme, 84.9, DiskVerdict.Warning)]
    [InlineData(DriveMedia.Ssd, DriveBus.Nvme, 85.0, DiskVerdict.Critical)]
    [InlineData(DriveMedia.Hdd, DriveBus.Sata, 54.9, DiskVerdict.Good)]
    [InlineData(DriveMedia.Hdd, DriveBus.Sata, 55.0, DiskVerdict.Warning)]
    [InlineData(DriveMedia.Hdd, DriveBus.Sata, 64.9, DiskVerdict.Warning)]
    [InlineData(DriveMedia.Hdd, DriveBus.Sata, 65.0, DiskVerdict.Critical)]
    public void TemperatureLimitsDependOnTheKindOfDisk(DriveMedia media, DriveBus bus, double temperature, DiskVerdict expected)
    {
        var result = DiskHealthEvaluator.Evaluate(Disk(media, bus, counters: Counters(temperature: temperature)));

        Assert.Equal(expected, result.Verdict);
    }

    [Fact]
    public void AnNvmeDriveCountsAsSolidStateEvenWithoutTheMediaType()
    {
        var disk = Disk(DriveMedia.Unknown, DriveBus.Nvme, counters: Counters(temperature: 60));

        Assert.True(disk.IsSolidState);
        Assert.Equal(DiskVerdict.Good, DiskHealthEvaluator.Evaluate(disk).Verdict);
    }

    [Fact]
    public void UncorrectedErrorsWarnWithTheirCounts()
    {
        var result = DiskHealthEvaluator.Evaluate(Disk(counters: Counters(readUncorrected: 3, writeUncorrected: 1)));

        Assert.Equal(DiskVerdict.Warning, result.Verdict);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingKind.UncorrectedErrors, finding.Kind);
        Assert.Equal(3, finding.Value);
        Assert.Equal(1, finding.Second);
    }

    [Fact]
    public void ZeroUncorrectedErrorsAreNotAFinding()
    {
        Assert.Empty(DiskHealthEvaluator.Evaluate(Disk(counters: Counters(readUncorrected: 0, writeUncorrected: 0, hours: 100))).Findings);
    }

    [Fact]
    public void CriticalBeatsWarningAndKeepsBothFindings()
    {
        var result = DiskHealthEvaluator.Evaluate(Disk(
            DriveMedia.Ssd, DriveBus.Nvme, WindowsHealth.Warning, predicted: true, counters: Counters(temperature: 90, wear: 85)));

        Assert.Equal(DiskVerdict.Critical, result.Verdict);
        Assert.Equal(
            [FindingKind.FailurePredicted, FindingKind.WindowsWarning, FindingKind.WearHigh, FindingKind.TemperatureCritical],
            result.Findings.Select(f => f.Kind));
    }
}

public sealed class StorageCollectorTests
{
    private const string Pc = "\\\\DESKTOP-TLC691T\\root\\cimv2:";

    private static string DiskRef(int index) => $"{Pc}Win32_DiskDrive.DeviceID=\"\\\\\\\\.\\\\PHYSICALDRIVE{index}\"";

    private static string PartitionRef(string name) => $"{Pc}Win32_DiskPartition.DeviceID=\"{name}\"";

    private static string LetterRef(string letter) => $"{Pc}Win32_LogicalDisk.DeviceID=\"{letter}\"";

    private static FakeWmiReader RealMachine() => new FakeWmiReader()
        .With(
            "Win32_DiskDrive",
            WmiRow.Of(("Index", 1u), ("Model", "NX-512 2280"), ("SerialNumber", "0000_0000_0000_0001."), ("FirmwareRevision", "SN6329"), ("InterfaceType", "SCSI"), ("Size", 512105932800UL), ("PNPDeviceID", @"SCSI\DISK&VEN_NVME&PROD_NX-512_2280\5&7014638&0&000000")),
            WmiRow.Of(("Index", 0u), ("Model", "ST1000VM002-1SD102"), ("SerialNumber", "            Z9C7SZ0N"), ("FirmwareRevision", "SC11"), ("InterfaceType", "IDE"), ("Size", 1000202273280UL), ("PNPDeviceID", @"SCSI\DISK&VEN_&PROD_ST1000VM002-1SD1\5&3955AA6&0&010000")))
        .With("MSFT_PhysicalDisk", WmiRow.Of(("DeviceId", "1"), ("HealthStatus", (ushort)0), ("MediaType", (ushort)4), ("BusType", (ushort)17)), WmiRow.Of(("DeviceId", "0"), ("HealthStatus", (ushort)0), ("MediaType", (ushort)3), ("BusType", (ushort)11)))
        .With("Win32_DiskDriveToDiskPartition", WmiRow.Of(("Antecedent", DiskRef(0)), ("Dependent", PartitionRef("Disk #0, Partition #0"))), WmiRow.Of(("Antecedent", DiskRef(1)), ("Dependent", PartitionRef("Disk #1, Partition #1"))))
        .With("Win32_LogicalDiskToPartition", WmiRow.Of(("Antecedent", PartitionRef("Disk #1, Partition #1")), ("Dependent", LetterRef("C:"))), WmiRow.Of(("Antecedent", PartitionRef("Disk #0, Partition #0")), ("Dependent", LetterRef("D:"))))
        .With("Win32_LogicalDisk", WmiRow.Of(("DeviceID", "D:"), ("VolumeName", "wasd"), ("FileSystem", "NTFS"), ("Size", 1000203087872UL), ("FreeSpace", 604448788480UL), ("DriveType", 3u)), WmiRow.Of(("DeviceID", "C:"), ("VolumeName", string.Empty), ("FileSystem", "NTFS"), ("Size", 508846665728UL), ("FreeSpace", 59918897152UL), ("DriveType", 3u)))
        .Failing("MSFT_StorageReliabilityCounter", new UnauthorizedAccessException("negado"))
        .Failing("MSStorageDriver_FailurePredictStatus", new UnauthorizedAccessException("negado"));

    private static StorageCollector Collector(FakeWmiReader wmi, CollectingLogger<StorageCollector>? logger = null, bool elevated = true) =>
        new(wmi, new ElevationInfo(elevated, "S-1-5-21-1", "PC-usuario"), logger ?? new CollectingLogger<StorageCollector>());

    [Fact]
    public void ReadsTheTwoDisksOfThisMachineInOrder()
    {
        var inventory = Collector(RealMachine()).ReadDisks();

        Assert.Equal([0, 1], inventory.Disks.Select(d => d.Index));
        var hdd = inventory.Disks[0];
        Assert.Equal("ST1000VM002-1SD102", hdd.Model);
        Assert.Equal(DriveMedia.Hdd, hdd.Media);
        Assert.Equal(DriveBus.Sata, hdd.Bus);
        Assert.Equal(WindowsHealth.Healthy, hdd.Health);
        Assert.Equal(1000202273280UL, hdd.SizeBytes);
        Assert.Equal(["D:"], hdd.Letters);
        Assert.Equal("Z9C7SZ0N", hdd.Serial.Display(reveal: true));
        var ssd = inventory.Disks[1];
        Assert.Equal(DriveMedia.Ssd, ssd.Media);
        Assert.Equal(DriveBus.Nvme, ssd.Bus);
        Assert.True(ssd.IsSolidState);
        Assert.Equal(["C:"], ssd.Letters);
        Assert.Equal("SN6329", ssd.Firmware);
    }

    [Fact]
    public void WithoutAdministratorTheCountersAndThePredictionAreFlaggedNotInvented()
    {
        var logger = new CollectingLogger<StorageCollector>();

        var inventory = Collector(RealMachine(), logger).ReadDisks();

        Assert.Equal(CounterAccess.NeedsAdministrator, inventory.CounterAccess);
        Assert.Equal(CounterAccess.NeedsAdministrator, inventory.PredictionAccess);
        Assert.All(inventory.Disks, d => Assert.Null(d.Counters));
        Assert.All(inventory.Disks, d => Assert.Null(d.FailurePredicted));
        Assert.All(logger.Entries, e => Assert.Equal(LogLevel.Information, e.Level));
        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public void WithAdministratorTheCountersAreReadAndUnsupportedValuesAreDropped()
    {
        var wmi = RealMachine()
            .WithRelated("MSFT_StorageReliabilityCounter", "1", WmiRow.Of(("Temperature", 38u), ("TemperatureMax", 70u), ("Wear", 4u), ("PowerOnHours", 4321UL), ("ReadErrorsTotal", 0UL), ("WriteErrorsTotal", 0UL), ("ReadErrorsUncorrected", 0UL), ("WriteErrorsUncorrected", 0UL), ("StartStopCycleCount", 812u)))
            .WithRelated("MSFT_StorageReliabilityCounter", "0", WmiRow.Of(("Temperature", 0xFFFFFFFFFFFFFFFFUL), ("Wear", -1), ("PowerOnHours", 9000UL)))
            .With("MSStorageDriver_FailurePredictStatus", WmiRow.Of(("InstanceName", @"SCSI\DISK&VEN_NVME&PROD_NX-512_2280\5&7014638&0&000000_0"), ("PredictFailure", false)));

        var inventory = Collector(wmi).ReadDisks();

        Assert.Equal(CounterAccess.Read, inventory.CounterAccess);
        var hdd = inventory.Disks[0];
        Assert.Null(hdd.Counters!.Temperature);
        Assert.Null(hdd.Counters.Wear);
        Assert.Equal(9000, hdd.Counters.PowerOnHours);
        var ssd = inventory.Disks[1];
        Assert.Equal(38, ssd.Counters!.Temperature);
        Assert.Equal(70, ssd.Counters.TemperatureMax);
        Assert.Equal(4, ssd.Counters.Wear);
        Assert.Equal(812, ssd.Counters.StartStopCycles);
        Assert.False(ssd.FailurePredicted);
        Assert.Null(hdd.FailurePredicted);
    }

    [Fact]
    public void TheCountersAreReadThroughTheAssociationWithEachPhysicalDisk()
    {
        var wmi = RealMachine()
            .WithRelated("MSFT_StorageReliabilityCounter", "1", WmiRow.Of(("Temperature", 41u), ("TemperatureMax", 90u), ("Wear", 0u)))
            .WithRelated("MSFT_StorageReliabilityCounter", "0", WmiRow.Of(("Temperature", 27u), ("TemperatureMax", 0u), ("Wear", 0u), ("PowerOnHours", 7382UL), ("ReadErrorsTotal", 0UL), ("StartStopCycleCount", 3067u)));

        var inventory = Collector(wmi).ReadDisks();

        Assert.Equal(("MSFT_PhysicalDisk", "MSFT_StorageReliabilityCounter"), Assert.Single(wmi.RelatedQueries));
        Assert.DoesNotContain(wmi.Queries, q => q.ClassName == "MSFT_StorageReliabilityCounter");
        var hdd = inventory.Disks[0];
        Assert.Equal(27, hdd.Counters!.Temperature);
        Assert.Equal(7382, hdd.Counters.PowerOnHours);
        Assert.Equal(3067, hdd.Counters.StartStopCycles);
        var ssd = inventory.Disks[1];
        Assert.Equal(41, ssd.Counters!.Temperature);
        Assert.Equal(90, ssd.Counters.TemperatureMax);
        Assert.Equal(0, ssd.Counters.Wear);
    }

    [Fact]
    public void AMechanicalDiskNeverReportsWearEvenWhenWindowsSendsZero()
    {
        var wmi = RealMachine()
            .WithRelated("MSFT_StorageReliabilityCounter", "1", WmiRow.Of(("Temperature", 41u), ("Wear", 0u)))
            .WithRelated("MSFT_StorageReliabilityCounter", "0", WmiRow.Of(("Temperature", 27u), ("Wear", 0u), ("PowerOnHours", 7382UL)));

        var inventory = Collector(wmi).ReadDisks();

        Assert.Null(inventory.Disks[0].Counters!.Wear);
        Assert.Equal(7382, inventory.Disks[0].Counters!.PowerOnHours);
        Assert.Equal(0, inventory.Disks[1].Counters!.Wear);
    }

    [Fact]
    public void WithoutAnyRelatedCounterAndWithoutAdministratorTheAnswerIsThatAdministratorIsNeeded()
    {
        var logger = new CollectingLogger<StorageCollector>();
        var inventory = Collector(RealMachine().With("MSFT_StorageReliabilityCounter"), logger, elevated: false).ReadDisks();

        Assert.All(inventory.Disks, d => Assert.Null(d.Counters));
        Assert.Equal(CounterAccess.NeedsAdministrator, inventory.CounterAccess);
        Assert.Contains(logger.Entries, e => e.EventId.Id == 3002);
    }

    [Fact]
    public void WithoutAnyRelatedCounterWhileElevatedTheSourceIsUnavailable()
    {
        var inventory = Collector(RealMachine().With("MSFT_StorageReliabilityCounter"), elevated: true).ReadDisks();

        Assert.All(inventory.Disks, d => Assert.Null(d.Counters));
        Assert.Equal(CounterAccess.Unavailable, inventory.CounterAccess);
    }

    [Fact]
    public void AnyMatchingPredictionOfFailureWins()
    {
        var pnp = @"SCSI\DISK&VEN_&PROD_ST1000VM002-1SD1\5&3955AA6&0&010000";
        var wmi = RealMachine().With("MSStorageDriver_FailurePredictStatus", WmiRow.Of(("InstanceName", pnp + "_0"), ("PredictFailure", false)), WmiRow.Of(("InstanceName", pnp + "_1"), ("PredictFailure", true)));

        var hdd = Collector(wmi).ReadDisks().Disks[0];

        Assert.True(hdd.FailurePredicted);
    }

    [Fact]
    public void AnUnsupportedCounterClassIsMarkedUnavailableAndLogged()
    {
        var logger = new CollectingLogger<StorageCollector>();
        var wmi = RealMachine().Failing("MSFT_StorageReliabilityCounter", new ManagementException("Classe inválida."));

        var inventory = Collector(wmi, logger).ReadDisks();

        Assert.Equal(CounterAccess.Unavailable, inventory.CounterAccess);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.EventId.Id == 3003);
    }

    [Fact]
    public void WithoutTheStorageApiTheDiskStillAppearsWithUnknownKindAndHealth()
    {
        var wmi = RealMachine().Failing("MSFT_PhysicalDisk", new ManagementException("Sem API."));

        var disk = Collector(wmi).ReadDisks().Disks[1];

        Assert.Equal(DriveMedia.Unknown, disk.Media);
        Assert.Equal(WindowsHealth.Unknown, disk.Health);
        Assert.Equal(DriveBus.Unknown, disk.Bus);
    }

    [Fact]
    public void AUsbStorageDriveIsRecognizedByItsPnpId()
    {
        var wmi = new FakeWmiReader().With("Win32_DiskDrive", WmiRow.Of(("Index", 2u), ("Model", "Pendrive"), ("Size", 16000000000UL), ("PNPDeviceID", @"USBSTOR\DISK&VEN_SANDISK\0")))
            .Failing("MSFT_PhysicalDisk", new UnauthorizedAccessException("negado"));

        var disk = Collector(wmi).ReadDisks().Disks[0];

        Assert.Equal(DriveBus.Usb, disk.Bus);
    }

    [Fact]
    public void WithoutAnyDiskTheReadFails()
    {
        Assert.Empty(Collector(new FakeWmiReader()).ReadDisks().Disks);
    }

    [Fact]
    public void VolumesAreSortedAndLinkedToTheirDisk()
    {
        var collector = Collector(RealMachine());
        var disks = collector.ReadDisks().Disks;

        var volumes = collector.ReadVolumes(disks);

        Assert.Equal(["C:", "D:"], volumes.Select(v => v.Letter));
        var c = volumes[0];
        Assert.Equal(VolumeKind.Internal, c.Kind);
        Assert.Equal("NX-512 2280", c.DiskModel);
        Assert.Equal(508846665728UL, c.TotalBytes);
        Assert.Equal(508846665728UL - 59918897152UL, c.UsedBytes);
        Assert.Equal(0.8822, c.UsedFraction, 3);
        Assert.Equal("wasd", volumes[1].Label);
        Assert.Equal("ST1000VM002-1SD102", volumes[1].DiskModel);
        Assert.Equal("NTFS", volumes[1].FileSystem);
    }

    [Fact]
    public void VolumesWithoutSizeAreSkipped()
    {
        var wmi = new FakeWmiReader().With("Win32_LogicalDisk", WmiRow.Of(("DeviceID", "E:"), ("DriveType", 5u)), WmiRow.Of(("DeviceID", "F:"), ("Size", 100UL), ("FreeSpace", 40UL), ("DriveType", 4u)));

        var volumes = Collector(wmi).ReadVolumes([]);

        var network = Assert.Single(volumes);
        Assert.Equal("F:", network.Letter);
        Assert.Equal(VolumeKind.Network, network.Kind);
        Assert.Equal(string.Empty, network.DiskModel);
    }

    [Theory]
    [InlineData(17, DriveBus.Nvme)]
    [InlineData(11, DriveBus.Sata)]
    [InlineData(7, DriveBus.Usb)]
    [InlineData(10, DriveBus.Sas)]
    [InlineData(8, DriveBus.Raid)]
    [InlineData(3, DriveBus.Other)]
    public void BusCodesAreMapped(int code, DriveBus expected)
    {
        Assert.Equal(expected, StorageCollector.BusOf(code));
    }

    [Fact]
    public void ABusCodeOfZeroMeansUnknown()
    {
        Assert.Null(StorageCollector.BusOf(0));
    }

    [Theory]
    [InlineData(3, DriveMedia.Hdd)]
    [InlineData(4, DriveMedia.Ssd)]
    [InlineData(5, DriveMedia.Scm)]
    [InlineData(0, DriveMedia.Unknown)]
    public void MediaCodesAreMapped(int code, DriveMedia expected)
    {
        Assert.Equal(expected, StorageCollector.MediaOf(code));
    }

    [Theory]
    [InlineData(0, WindowsHealth.Healthy)]
    [InlineData(1, WindowsHealth.Warning)]
    [InlineData(2, WindowsHealth.Unhealthy)]
    [InlineData(5, WindowsHealth.Unknown)]
    public void HealthCodesAreMapped(int code, WindowsHealth expected)
    {
        Assert.Equal(expected, StorageCollector.HealthOf(code));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(2e12)]
    public void CountersOutOfRangeMeanNotSupported(double raw)
    {
        Assert.Null(StorageCollector.Counter(WmiRow.Of(("X", raw)), "X"));
    }

    [Fact]
    public void ACounterOfZeroIsARealValue()
    {
        Assert.Equal(0.0, StorageCollector.Counter(WmiRow.Of(("X", 0u)), "X"));
        Assert.Null(StorageCollector.Counter(WmiRow.Of(), "X"));
    }
}

public sealed class StorageServiceTests
{
    [Fact]
    public async Task FailedDisksStillLetTheVolumesShow()
    {
        var wmi = new FakeWmiReader()
            .Failing("Win32_DiskDrive", new TimeoutException("Demorou."))
            .With("Win32_LogicalDisk", WmiRow.Of(("DeviceID", "C:"), ("Size", 100UL), ("FreeSpace", 50UL), ("DriveType", 3u)));
        var service = new StorageService(new StorageCollector(wmi, ElevationInfo.Current(), NullLogger<StorageCollector>.Instance), NullLogger<StorageService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(report.Disks.IsSuccess);
        Assert.Equal("Demorou.", report.Disks.Error);
        Assert.Equal(CounterAccess.Unavailable, report.CounterAccess);
        Assert.True(report.Volumes.TryGet(out var volumes));
        Assert.Single(volumes);
    }

    [Fact]
    public async Task EachDiskComesWithItsAssessment()
    {
        var wmi = new FakeWmiReader()
            .With("Win32_DiskDrive", WmiRow.Of(("Index", 0u), ("Model", "SSD"), ("Size", 1000UL), ("PNPDeviceID", "X")))
            .With("MSFT_PhysicalDisk", WmiRow.Of(("DeviceId", "0"), ("HealthStatus", (ushort)2), ("MediaType", (ushort)4), ("BusType", (ushort)17)))
            .Failing("MSFT_StorageReliabilityCounter", new UnauthorizedAccessException("negado"))
            .Failing("MSStorageDriver_FailurePredictStatus", new UnauthorizedAccessException("negado"));
        var service = new StorageService(new StorageCollector(wmi, ElevationInfo.Current(), NullLogger<StorageCollector>.Instance), NullLogger<StorageService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Disks.TryGet(out var disks));
        var disk = Assert.Single(disks);
        Assert.Equal(DiskVerdict.Critical, disk.Assessment.Verdict);
        Assert.Equal(CounterAccess.NeedsAdministrator, report.CounterAccess);
    }

    [Fact]
    public async Task ReadsThisMachineForReal()
    {
        var service = new StorageService(new StorageCollector(new WindowsWmiReader(), ElevationInfo.Current(), NullLogger<StorageCollector>.Instance), NullLogger<StorageService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Disks.TryGet(out var disks), report.Disks.Error);
        Assert.NotEmpty(disks);
        Assert.All(disks, d => Assert.False(string.IsNullOrWhiteSpace(d.Disk.Model)));
        Assert.All(disks, d => Assert.True(d.Disk.SizeBytes > 0));
        Assert.True(report.Volumes.TryGet(out var volumes), report.Volumes.Error);
        Assert.Contains(volumes, v => v.Letter.Equals("C:", StringComparison.OrdinalIgnoreCase));
        Assert.All(volumes, v => Assert.InRange(v.UsedFraction, 0, 1));
        Assert.NotEqual(CounterAccess.Unavailable, report.CounterAccess);
    }

    [Fact]
    public async Task WithAdministratorTheRealCountersArePairedWithTheirDisks()
    {
        Assert.SkipUnless(ElevationInfo.Current().IsElevated, "Precisa de administrador.");
        var service = new StorageService(new StorageCollector(new WindowsWmiReader(), ElevationInfo.Current(), NullLogger<StorageCollector>.Instance), NullLogger<StorageService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CounterAccess.Read, report.CounterAccess);
        Assert.True(report.Disks.TryGet(out var disks), report.Disks.Error);
        Assert.All(disks, d => Assert.NotNull(d.Disk.Counters));
        Assert.Contains(disks, d => d.Disk.Counters!.Temperature is > 0 and < 100);
    }
}
