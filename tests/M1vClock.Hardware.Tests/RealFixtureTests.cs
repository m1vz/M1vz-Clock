using M1vClock.Core.Security;
using M1vClock.Hardware.Devices;
using M1vClock.Hardware.Gpu;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Ram;
using M1vClock.Hardware.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class RealStorageFixtureTests
{
    private static readonly FixtureWmiReader Wmi = FixtureWmiReader.Load("wmi-desktop-amd");
    private static readonly ElevationInfo Administrator = new(true, "S-1-5-21-1", "PC-EXEMPLO\\usuario");

    private static StorageCollector Collector() => new(Wmi, Administrator, new CollectingLogger<StorageCollector>());

    [Fact]
    public void TheRealDisksAreReadInOrderWithTheirKindAndBus()
    {
        var inventory = Collector().ReadDisks();

        Assert.Equal(CounterAccess.Read, inventory.CounterAccess);
        Assert.Equal(CounterAccess.Read, inventory.PredictionAccess);
        var hdd = inventory.Disks[0];
        Assert.Equal("ST1000VM002-1SD102", hdd.Model);
        Assert.Equal(DriveMedia.Hdd, hdd.Media);
        Assert.Equal(DriveBus.Sata, hdd.Bus);
        Assert.Equal(["D:"], hdd.Letters);
        var ssd = inventory.Disks[1];
        Assert.Equal("NX-512 2280", ssd.Model);
        Assert.Equal(DriveMedia.Ssd, ssd.Media);
        Assert.Equal(DriveBus.Nvme, ssd.Bus);
        Assert.Equal(["C:"], ssd.Letters);
    }

    [Fact]
    public void TheRealCountersArePairedThroughTheAssociationWithEachDisk()
    {
        var inventory = Collector().ReadDisks();

        var hdd = inventory.Disks[0].Counters!;
        Assert.Equal(27, hdd.Temperature);
        Assert.Equal(7384, hdd.PowerOnHours);
        Assert.Equal(3067, hdd.StartStopCycles);
        Assert.Equal(0, hdd.ReadErrors);
        Assert.Null(hdd.Wear);
        var ssd = inventory.Disks[1].Counters!;
        Assert.Equal(41, ssd.Temperature);
        Assert.Equal(90, ssd.TemperatureMax);
        Assert.Equal(0, ssd.Wear);
        Assert.Null(ssd.PowerOnHours);
    }

    [Fact]
    public void ThePredictionOfFailureOnlyExistsForTheDiskWindowsReportsItFor()
    {
        var inventory = Collector().ReadDisks();

        Assert.False(inventory.Disks[0].FailurePredicted);
        Assert.Null(inventory.Disks[1].FailurePredicted);
    }

    [Fact]
    public void TheCountersMatchWhatTheGetStorageReliabilityCounterCmdletPrinted()
    {
        using var cmdlet = FixtureWmiReader.LoadRaw("get-storagereliabilitycounter-desktop-amd");
        var inventory = Collector().ReadDisks();

        foreach (var printed in cmdlet.RootElement.EnumerateArray())
        {
            var disk = inventory.Disks[int.Parse(printed.GetProperty("diskNumber").GetString()!, System.Globalization.CultureInfo.InvariantCulture)];
            Assert.Equal(printed.GetProperty("friendlyName").GetString(), disk.Model);
            Assert.Equal(printed.GetProperty("temperature").GetDouble(), disk.Counters!.Temperature);
            if (printed.GetProperty("powerOnHours").ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                Assert.Equal(printed.GetProperty("powerOnHours").GetInt64(), disk.Counters.PowerOnHours);
            }
            else
            {
                Assert.Null(disk.Counters.PowerOnHours);
            }
        }
    }

    [Fact]
    public void TheRealVolumesCarryTheirLabelsAndSizes()
    {
        var collector = Collector();
        var volumes = collector.ReadVolumes(collector.ReadDisks().Disks);

        Assert.Equal(["C:", "D:"], volumes.Select(v => v.Letter));
        Assert.Equal(508_846_665_728UL, volumes[0].TotalBytes);
        Assert.Equal("wasd", volumes[1].Label);
        Assert.Equal("NX-512 2280", volumes[0].DiskModel);
        Assert.All(volumes, v => Assert.Equal(VolumeKind.Internal, v.Kind));
    }

    [Fact]
    public async Task BothRealDisksAreHealthyAndTheVerdictIsGood()
    {
        var service = new StorageService(Collector(), new StubSensorService(), TimeProvider.System, NullLogger<StorageService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Disks.TryGet(out var disks), report.Disks.Error);
        Assert.All(disks, d => Assert.Equal(DiskVerdict.Good, d.Assessment.Verdict));
    }

    [Fact]
    public void WithoutAdministratorThisSameRealDataIsStillReadButNothingIsHidden()
    {
        var inventory = new StorageCollector(Wmi, new ElevationInfo(false, "S-1-5-21-1", "PC-EXEMPLO\\usuario"), new CollectingLogger<StorageCollector>()).ReadDisks();

        Assert.Equal(CounterAccess.Read, inventory.CounterAccess);
        Assert.NotNull(inventory.Disks[0].Counters);
    }
}

public sealed class RealPlatformFixtureTests
{
    private static readonly FixtureWmiReader Wmi = FixtureWmiReader.Load("wmi-desktop-amd");

    private static PlatformCollector Collector() => new(Wmi, new StubRegistry(), "PC-EXEMPLO");

    [Fact]
    public void TheRealOperatingSystemIsWindows11()
    {
        var os = Collector().ReadOs();

        Assert.Equal("Windows 11 Home", os.Name);
        Assert.Equal("26200", os.Build);
        Assert.Equal("PC-EXEMPLO", os.ComputerName);
    }

    [Fact]
    public void TheRealProcessorIsAnEightCoreRyzen()
    {
        var cpu = Assert.Single(Collector().ReadCpus());

        Assert.Equal("AMD Ryzen 7 5700X 8-Core Processor", cpu.Name);
        Assert.Equal(8, cpu.Cores);
        Assert.Equal(16, cpu.Threads);
        Assert.Equal(3401, cpu.MaxClockMhz);
        Assert.Equal(4096, cpu.L2CacheKb);
        Assert.Equal(32768, cpu.L3CacheKb);
        Assert.Equal("AMD", cpu.Manufacturer);
    }

    [Fact]
    public void TheRealBoardAndBiosAreRead()
    {
        var board = Collector().ReadBoard();
        var bios = Collector().ReadBios();

        Assert.Contains("ASUS", board.Manufacturer, StringComparison.Ordinal);
        Assert.Equal("TUF GAMING B550M-PLUS", board.Model);
        Assert.Equal("3636", bios.Version);
        Assert.Equal(new DateOnly(2026, 1, 4), bios.ReleaseDate);
    }
}

public sealed class RealRamFixtureTests
{
    private static readonly FixtureWmiReader Wmi = FixtureWmiReader.Load("wmi-desktop-amd");

    [Fact]
    public void TheRealMemoryIsTwoEightGigabyteDdr4ModulesInTwoOfFourSlots()
    {
        var inventory = new RamCollector(Wmi).ReadInventory();

        Assert.Equal(2, inventory.Summary.Installed);
        Assert.Equal(4, inventory.Summary.Slots);
        Assert.Equal(16UL * 1024 * 1024 * 1024, inventory.Summary.TotalBytes);
        Assert.Equal([RamType.Ddr4], inventory.Summary.Types);
        Assert.Equal(["DIMM_A2", "DIMM_B2"], inventory.Modules.Select(m => m.Locator));
        Assert.All(inventory.Modules, m =>
        {
            Assert.Equal(8UL * 1024 * 1024 * 1024, m.CapacityBytes);
            Assert.Equal(3200, m.RatedSpeed);
            Assert.Equal(3200, m.ConfiguredSpeed);
            Assert.Equal("Kingston", m.Manufacturer);
        });
    }

    [Fact]
    public void TheRealModulesSitInDifferentChannels()
    {
        var channels = new RamCollector(Wmi).ReadInventory().Summary.Channels;

        Assert.Equal(ChannelVerdict.MultiChannel, channels.Verdict);
        Assert.Equal(2, channels.Channels);
    }
}

public sealed class RealGpuFixtureTests
{
    private static readonly FixtureWmiReader Wmi = FixtureWmiReader.Load("wmi-desktop-amd");

    private sealed class Dxgi(params DxgiAdapter[] adapters) : IDxgiAdapterSource
    {
        public IReadOnlyList<DxgiAdapter> Enumerate() => adapters;
    }

    private sealed class Libraries : ISystemLibraries
    {
        public bool Exists(string fileName) => true;
    }

    private static GpuInfo Gpu() =>
        Assert.Single(new GpuCollector(
            Wmi,
            new Dxgi(new DxgiAdapter("AMD Radeon RX 5500 XT", 0x1002, 0x7340, 8176UL * 1024 * 1024, 0, 8UL * 1024 * 1024 * 1024, "12_1")),
            new Libraries(),
            GpuSpecDatabase.Default,
            new CollectingLogger<GpuCollector>()).Read());

    [Fact]
    public void TheRealRadeonIsIdentifiedByItsPciIdAndVendor()
    {
        var gpu = Gpu();

        Assert.Equal("AMD Radeon RX 5500 XT", gpu.Name);
        Assert.Equal("AMD", gpu.VendorName);
        Assert.Equal("1002:7340", gpu.Pci!.Key);
        Assert.Equal("C5", gpu.Pci.Revision);
        Assert.NotNull(gpu.Spec);
    }

    [Fact]
    public void TheRealVideoMemoryComesFromDxgiBecauseWmiCapsAtFourGigabytes()
    {
        var gpu = Gpu();

        Assert.Equal(8176UL * 1024 * 1024, gpu.VideoMemoryBytes);
        Assert.Equal(8UL, GpuCollector.MarketedGigabytes(gpu.VideoMemoryBytes));
    }

    [Fact]
    public void TheRealDriverIsSignedByMicrosoftAndItsDateWithWildcardsDoesNotBreakTheRead()
    {
        var gpu = Gpu();

        Assert.Equal("32.0.21045.16010", gpu.DriverVersion);
        Assert.Equal(DriverSignature.Whql, gpu.Signature);
        Assert.Equal(new DateOnly(2026, 10, 16), gpu.DriverDate);
    }
}

public sealed class RealUsbFixtureTests
{
    private static readonly FixtureWmiReader Wmi = FixtureWmiReader.Load("wmi-desktop-amd");

    [Fact]
    public void TheRealUsbDevicesAreGroupedWithoutRootHubsOrInterfaceDuplicates()
    {
        var devices = new UsbCollector(Wmi).ReadDevices();

        Assert.NotEmpty(devices);
        Assert.All(devices, d => Assert.StartsWith("USB\\VID_", d.InstanceId, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(devices, d => d.InstanceId.Contains("ROOT_HUB", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(devices, d => d.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(devices.Count, devices.Select(d => d.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TheRealDevicesTakeTheNameOfTheirMostDescriptiveInterface()
    {
        var devices = new UsbCollector(Wmi).ReadDevices();

        var aura = Assert.Single(devices, d => d.Name == "AURA LED Controller");
        Assert.Equal("ASUS", aura.Maker);
        var microphone = Assert.Single(devices, d => d.Name == "Fifine Microphone");
        Assert.Equal(UsbCategory.Audio, microphone.Category);
        Assert.Contains(devices, d => d.Category == UsbCategory.Hub);
        Assert.Contains(devices, d => d.Category == UsbCategory.Input);
        Assert.Equal(UsbCategory.Hub, devices[^1].Category);
    }

    [Fact]
    public void NoRealDeviceIsFlaggedWithAProblem()
    {
        Assert.All(new UsbCollector(Wmi).ReadDevices(), d => Assert.False(d.HasProblem, d.Name));
    }
}
