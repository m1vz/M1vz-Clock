using System.Globalization;
using System.Text.RegularExpressions;
using M1vClock.Hardware.Gpu;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class PciIdTests
{
    [Fact]
    public void ParsesTheRealRadeonOfThisMachine()
    {
        var pci = PciId.Parse(@"PCI\VEN_1002&DEV_7340&SUBSYS_0B0C1002&REV_C5\6&26A46C08&0&00000019");

        Assert.NotNull(pci);
        Assert.Equal("1002", pci.VendorId);
        Assert.Equal("7340", pci.DeviceId);
        Assert.Equal("0B0C", pci.SubsystemDeviceId);
        Assert.Equal("1002", pci.SubsystemVendorId);
        Assert.Equal("C5", pci.Revision);
        Assert.Equal("1002:7340", pci.Key);
    }

    [Fact]
    public void ReadsTheBoardMakerFromTheSubsystemId()
    {
        var pci = PciId.Parse(@"PCI\VEN_10DE&DEV_2684&SUBSYS_88A41043&REV_A1\4&1&0&0008");

        Assert.Equal("1043", pci!.SubsystemVendorId);
        Assert.Equal("88A4", pci.SubsystemDeviceId);
    }

    [Fact]
    public void AMissingSubsystemOrRevisionBecomesEmptyText()
    {
        var pci = PciId.Parse(@"PCI\VEN_8086&DEV_46A6\3&11583659&0&10");

        Assert.Equal("8086", pci!.VendorId);
        Assert.Equal(string.Empty, pci.SubsystemVendorId);
        Assert.Equal(string.Empty, pci.Revision);
    }

    [Fact]
    public void LowercaseInputIsNormalized()
    {
        Assert.Equal("10DE:2684", PciId.Parse(@"pci\ven_10de&dev_2684")!.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"USB\VID_046D&PID_C52B")]
    public void NonPciIdsReturnNothing(string? pnp)
    {
        Assert.Null(PciId.Parse(pnp));
    }
}

public sealed partial class GpuSpecDatabaseTests
{
    private static readonly GpuSpecDatabase Db = GpuSpecDatabase.Default;

    [GeneratedRegex(@"^[0-9A-F]{4}:[0-9A-F]{4}$")]
    private static partial Regex IdPattern();

    [Fact]
    public void KnowsTheRadeonOfThisMachine()
    {
        var spec = Db.Find("1002", "7340", "AMD Radeon RX 5500 XT");

        Assert.NotNull(spec);
        Assert.Equal("Navi 14", spec.Chip);
        Assert.Equal("RDNA 1", spec.Architecture);
        Assert.Equal(1408, spec.Shaders);
        Assert.Equal(32, spec.Rops);
        Assert.Equal(88, spec.Tmus);
        Assert.Equal(5.196, spec.Fp32Teraflops(spec.Shaders), 3);
        Assert.Equal(59.04, spec.PixelFillrate, 2);
        Assert.Equal(162.36, spec.TextureFillrate, 2);
    }

    [Theory]
    [InlineData("AMD Radeon RX 9070 XT", 4096)]
    [InlineData("AMD Radeon RX 9070", 3584)]
    [InlineData("AMD Radeon RX 5700 XT", 2560)]
    [InlineData("AMD Radeon RX 5700", 2304)]
    [InlineData("AMD Radeon RX 6900 XT", 5120)]
    [InlineData("AMD Radeon RX 6800 XT", 4608)]
    [InlineData("AMD Radeon RX 6800", 3840)]
    public void TheNameBreaksTiesBetweenVariantsOfTheSameChip(string name, int shaders)
    {
        var id = name.Contains("9070", StringComparison.Ordinal) ? ("1002", "7550")
            : name.Contains("57", StringComparison.Ordinal) ? ("1002", "731F")
            : ("1002", "73BF");

        Assert.Equal(shaders, Db.Find(id.Item1, id.Item2, name)!.Shaders);
    }

    [Fact]
    public void WithoutANameMatchTheFirstVariantIsUsed()
    {
        Assert.Equal(4096, Db.Find("1002", "7550", "Radeon desconhecida")!.Shaders);
    }

    [Fact]
    public void UnknownCardsHaveNoSpec()
    {
        Assert.Null(Db.Find("10DE", "FFFF", "GeForce"));
        Assert.Null(Db.Find("1234", "5678", "x"));
    }

    [Fact]
    public void IdLookupIsCaseInsensitive()
    {
        Assert.NotNull(Db.Find("1002", "731f", "RX 5700 XT"));
    }

    [Fact]
    public void EveryIdLooksLikeAPciVendorAndDevice()
    {
        Assert.All(Db.Ids, id => Assert.Matches(IdPattern(), id));
    }

    [Fact]
    public void EverySpecIsInternallyConsistent()
    {
        Assert.All(Db.All, spec =>
        {
            var label = $"{spec.Chip} {spec.NameMatch}";
            Assert.False(string.IsNullOrWhiteSpace(spec.Chip), label);
            Assert.False(string.IsNullOrWhiteSpace(spec.Architecture), label);
            Assert.False(string.IsNullOrWhiteSpace(spec.Process), label);
            Assert.True(spec.DieMm2 > 0, label);
            Assert.True(spec.TransistorsBillions > 0, label);
            Assert.True(spec.Shaders > 0, label);
            Assert.True(spec.Rops > 0 && spec.Rops <= spec.Shaders, label);
            Assert.True(spec.Tmus > 0, label);
            Assert.False(string.IsNullOrWhiteSpace(spec.MemoryType), label);
            Assert.True(spec.BaseMhz > 0 && spec.BaseMhz <= spec.BoostMhz, label);
            Assert.True(spec.TdpWatts >= 0, label);
            Assert.True(DateTime.TryParseExact(spec.Release, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), $"{label}: data {spec.Release}");
        });
    }

    [Fact]
    public void EveryArchitectureHasADescriptionOfItsTechnologies()
    {
        var missing = Db.All.Select(s => s.Architecture).Distinct().Where(a => Db.FeaturesOf(a) is null).ToList();

        Assert.True(missing.Count == 0, "sem recursos descritos: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("1002", "AMD")]
    [InlineData("10DE", "NVIDIA")]
    [InlineData("8086", "Intel")]
    [InlineData("ABCD", "ABCD")]
    public void VendorsAreNamed(string id, string expected)
    {
        Assert.Equal(expected, Db.VendorName(id));
    }

    [Theory]
    [InlineData("1043", "ASUS")]
    [InlineData("1462", "MSI")]
    [InlineData("1002", "AMD")]
    [InlineData("FFFF", "FFFF")]
    public void BoardMakersAreNamed(string id, string expected)
    {
        Assert.Equal(expected, Db.SubvendorName(id));
    }

    [Fact]
    public void ABrokenFileIsRejected()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => GpuSpecDatabase.FromJson("{ não é json"));
        Assert.Throws<ArgumentException>(() => GpuSpecDatabase.FromJson(" "));
    }
}

public sealed class GpuCollectorTests
{
    private const ulong Mebibyte = 1024UL * 1024;
    private const string RadeonPnp = @"PCI\VEN_1002&DEV_7340&SUBSYS_0B0C1002&REV_C5\6&26A46C08&0&00000019";

    private static WmiRow Radeon() => WmiRow.Of(
        ("Name", "AMD Radeon RX 5500 XT"), ("PNPDeviceID", RadeonPnp), ("DriverVersion", "32.0.21045.16010"),
        ("DriverDate", "20261016000000.000000+000"), ("AdapterRAM", 4293918720UL));

    private static DxgiAdapter RadeonDxgi() => new("AMD Radeon RX 5500 XT", 0x1002, 0x7340, 8176 * Mebibyte, 0, 8 * 1024 * Mebibyte, "12_1");

    private static FakeWmiReader Wmi(params WmiRow[] gpus) => new FakeWmiReader()
        .With("Win32_VideoController", gpus)
        .With("Win32_PnPSignedDriver", WmiRow.Of(("DeviceID", RadeonPnp), ("IsSigned", true), ("Signer", "Microsoft Windows Hardware Compatibility Publisher")));

    private static GpuCollector Collector(FakeWmiReader wmi, IDxgiAdapterSource? dxgi = null, ISystemLibraries? libraries = null, CollectingLogger<GpuCollector>? logger = null) =>
        new(wmi, dxgi ?? new FakeDxgi(RadeonDxgi()), libraries ?? new FakeLibraries("OpenCL.dll", "vulkan-1.dll"), GpuSpecDatabase.Default, logger ?? new CollectingLogger<GpuCollector>());

    [Fact]
    public void BuildsTheRadeonOfThisMachineFromAllTheSources()
    {
        var gpu = Assert.Single(Collector(Wmi(Radeon())).Read());

        Assert.Equal("AMD Radeon RX 5500 XT", gpu.Name);
        Assert.Equal("AMD", gpu.VendorName);
        Assert.Equal("Navi 14", gpu.Spec!.Chip);
        Assert.Equal(8176 * Mebibyte, gpu.VideoMemoryBytes);
        Assert.Equal(8 * 1024 * Mebibyte, gpu.SharedMemoryBytes);
        Assert.Equal("32.0.21045.16010", gpu.DriverVersion);
        Assert.Equal(new DateOnly(2026, 10, 16), gpu.DriverDate);
        Assert.Equal(DriverSignature.Whql, gpu.Signature);
        Assert.Equal("12_1", gpu.FeatureLevel);
        Assert.Equal([GpuApi.OpenCl, GpuApi.Vulkan, GpuApi.DirectCompute, GpuApi.DirectMl], gpu.Apis);
        Assert.Contains("FidelityFX", gpu.ArchitectureFeatures, StringComparison.Ordinal);
        Assert.False(gpu.IsIntegrated);
    }

    [Fact]
    public void TheVideoMemoryComesFromDxgiBecauseWmiStopsAtFourGigabytes()
    {
        var gpu = Collector(Wmi(Radeon())).Read()[0];

        Assert.True(gpu.VideoMemoryBytes > 4UL * 1024 * Mebibyte);
        Assert.Equal(8UL, GpuCollector.MarketedGigabytes(gpu.VideoMemoryBytes));
    }

    [Theory]
    [InlineData(2048UL, 2UL)]
    [InlineData(4095UL, 4UL)]
    [InlineData(6141UL, 6UL)]
    [InlineData(8176UL, 8UL)]
    [InlineData(12227UL, 12UL)]
    [InlineData(16304UL, 16UL)]
    [InlineData(512UL, 0UL)]
    public void MarketedSizeUndoesTheDriverReservation(ulong mebibytes, ulong expected)
    {
        Assert.Equal(expected, GpuCollector.MarketedGigabytes(mebibytes * Mebibyte));
    }

    [Fact]
    public void WithoutDxgiTheGpuStillShowsWithTheWmiMemoryAndNoFeatureLevel()
    {
        var logger = new CollectingLogger<GpuCollector>();

        var gpu = Collector(Wmi(Radeon()), new FakeDxgi(new InvalidOperationException("sem d3d12")), logger: logger).Read()[0];

        Assert.Equal(4293918720UL, gpu.VideoMemoryBytes);
        Assert.Null(gpu.FeatureLevel);
        Assert.DoesNotContain(GpuApi.DirectCompute, gpu.Apis);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(3003, entry.EventId.Id);
    }

    [Fact]
    public void TheDedicatedCardComesBeforeTheIntegratedOne()
    {
        var integrated = WmiRow.Of(("Name", "AMD Radeon(TM) Graphics"), ("PNPDeviceID", @"PCI\VEN_1002&DEV_164E&SUBSYS_88A41043&REV_C1\4&1"), ("DriverVersion", "32.0"), ("AdapterRAM", 536870912UL));
        var wmi = Wmi(integrated, Radeon());
        var dxgi = new FakeDxgi(new DxgiAdapter("iGPU", 0x1002, 0x164E, 512 * Mebibyte, 0, 8192 * Mebibyte, "12_1"), RadeonDxgi());

        var gpus = Collector(wmi, dxgi).Read();

        Assert.Equal(["AMD Radeon RX 5500 XT", "AMD Radeon(TM) Graphics"], gpus.Select(g => g.Name));
        Assert.True(gpus[1].IsIntegrated);
    }

    [Fact]
    public void TwoIdenticalCardsEachGetTheirOwnAdapter()
    {
        var wmi = Wmi(Radeon(), Radeon());
        var dxgi = new FakeDxgi(RadeonDxgi(), RadeonDxgi() with { DedicatedVideoBytes = 4096 * Mebibyte });

        var gpus = Collector(wmi, dxgi).Read();

        Assert.Equal([8176 * Mebibyte, 4096 * Mebibyte], gpus.Select(g => g.VideoMemoryBytes));
    }

    [Fact]
    public void ACardTheDatabaseDoesNotKnowKeepsAnEmptySpec()
    {
        var odd = WmiRow.Of(("Name", "Placa Misteriosa"), ("PNPDeviceID", @"PCI\VEN_10DE&DEV_FFFF&SUBSYS_00001043&REV_01\1"), ("DriverVersion", "1.0"));

        var gpu = Collector(Wmi(odd), new FakeDxgi()).Read()[0];

        Assert.Null(gpu.Spec);
        Assert.Null(gpu.ArchitectureFeatures);
        Assert.Equal("NVIDIA", gpu.VendorName);
        Assert.Contains(GpuApi.PhysX, gpu.Apis);
    }

    [Fact]
    public void ACardWithoutAPciIdKeepsItsNameAndAnUnknownVendor()
    {
        var virtualCard = WmiRow.Of(("Name", "Microsoft Basic Display Adapter"), ("PNPDeviceID", @"ROOT\BASICDISPLAY\0000"));

        var gpu = Collector(Wmi(virtualCard), new FakeDxgi()).Read()[0];

        Assert.Equal("—", gpu.VendorName);
        Assert.Null(gpu.Pci);
        Assert.Null(gpu.Spec);
    }

    [Theory]
    [InlineData(false, null, DriverSignature.NotSigned)]
    [InlineData(true, "Microsoft Windows Hardware Compatibility Publisher", DriverSignature.Whql)]
    [InlineData(true, "AMD Inc.", DriverSignature.VendorSigned)]
    public void TheDriverSignatureIsClassified(bool isSigned, string? signer, DriverSignature expected)
    {
        var wmi = new FakeWmiReader()
            .With("Win32_VideoController", Radeon())
            .With("Win32_PnPSignedDriver", WmiRow.Of(("DeviceID", RadeonPnp.ToLowerInvariant()), ("IsSigned", isSigned), ("Signer", signer)));

        var gpu = Collector(wmi).Read()[0];

        Assert.Equal(expected, gpu.Signature);
    }

    [Fact]
    public void ADriverThatIsNotListedHasAnUnknownSignature()
    {
        var wmi = new FakeWmiReader().With("Win32_VideoController", Radeon());

        Assert.Equal(DriverSignature.Unknown, Collector(wmi).Read()[0].Signature);
    }

    [Fact]
    public void ADeniedSignatureQueryIsLoggedAsInformationAndTheGpuStillAppears()
    {
        var logger = new CollectingLogger<GpuCollector>();
        var wmi = new FakeWmiReader().With("Win32_VideoController", Radeon()).Failing("Win32_PnPSignedDriver", new UnauthorizedAccessException("negado"));

        var gpu = Collector(wmi, logger: logger).Read()[0];

        Assert.Equal(DriverSignature.Unknown, gpu.Signature);
        Assert.Equal(LogLevel.Information, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public void CudaNeedsAnNvidiaCardAndItsLibrary()
    {
        var nvidia = WmiRow.Of(("Name", "GeForce RTX 4090"), ("PNPDeviceID", @"PCI\VEN_10DE&DEV_2684&SUBSYS_88A41043&REV_A1\1"), ("DriverVersion", "560.0"));
        var libraries = new FakeLibraries("nvcuda.dll", "OpenCL.dll");

        var nvidiaGpu = Collector(Wmi(nvidia), new FakeDxgi(new DxgiAdapter("RTX", 0x10DE, 0x2684, 24576 * Mebibyte, 0, 0, "12_2")), libraries).Read()[0];
        var amdGpu = Collector(Wmi(Radeon()), new FakeDxgi(RadeonDxgi()), libraries).Read()[0];

        Assert.Contains(GpuApi.Cuda, nvidiaGpu.Apis);
        Assert.Contains(GpuApi.PhysX, nvidiaGpu.Apis);
        Assert.DoesNotContain(GpuApi.Cuda, amdGpu.Apis);
        Assert.DoesNotContain(GpuApi.PhysX, amdGpu.Apis);
        Assert.Equal("12_2", nvidiaGpu.FeatureLevel);
    }

    [Fact]
    public void NoVideoControllerIsAnError()
    {
        Assert.Throws<HardwareReadException>(() => Collector(new FakeWmiReader()).Read());
    }

    [Fact]
    public async Task ReadsThisMachineForReal()
    {
        var collector = new GpuCollector(new WindowsWmiReader(), new VorticeDxgiAdapterSource(), new WindowsSystemLibraries(), GpuSpecDatabase.Default, NullLogger<GpuCollector>.Instance);
        var service = new GpuService(collector, NullLogger<GpuService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Gpus.TryGet(out var gpus), report.Gpus.Error);
        Assert.NotEmpty(gpus);
        Assert.All(gpus, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
        Assert.All(gpus, g => Assert.True(g.VideoMemoryBytes > 0));
        Assert.Contains(gpus, g => g.FeatureLevel is not null);
    }

    private sealed class FakeDxgi : IDxgiAdapterSource
    {
        private readonly DxgiAdapter[] _adapters;
        private readonly Exception? _failure;

        public FakeDxgi(params DxgiAdapter[] adapters) => _adapters = adapters;

        public FakeDxgi(Exception failure)
        {
            _adapters = [];
            _failure = failure;
        }

        public IReadOnlyList<DxgiAdapter> Enumerate() => _failure is null ? _adapters : throw _failure;
    }

    private sealed class FakeLibraries(params string[] present) : ISystemLibraries
    {
        public bool Exists(string fileName) => present.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }
}
