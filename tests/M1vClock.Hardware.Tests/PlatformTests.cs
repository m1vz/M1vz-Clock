using System.Management;
using M1vClock.Core.RegistryValues;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Tests;

public sealed class PlatformCollectorTests
{
    private static FakeWmiReader RealMachine() => new FakeWmiReader()
        .With("Win32_OperatingSystem", WmiRow.Of(("Caption", "Microsoft Windows 11 Home"), ("Version", "10.0.26200"), ("BuildNumber", "26200"), ("OSArchitecture", "64 bits")))
        .With("Win32_BaseBoard", WmiRow.Of(("Manufacturer", "ASUSTeK COMPUTER INC."), ("Product", "TUF GAMING B550M-PLUS"), ("Version", "Rev X.0x"), ("SerialNumber", "To be filled by O.E.M.")))
        .With("Win32_BIOS", WmiRow.Of(("Manufacturer", "American Megatrends Inc."), ("SMBIOSBIOSVersion", "3636"), ("ReleaseDate", "20260104000000.000000+000")))
        .With("Win32_Processor", WmiRow.Of(
            ("Name", "AMD Ryzen 7 5700X 8-Core Processor             "),
            ("Manufacturer", "AuthenticAMD"),
            ("SocketDesignation", "AM4"),
            ("NumberOfCores", 8u),
            ("NumberOfLogicalProcessors", 16u),
            ("MaxClockSpeed", 3401u),
            ("L2CacheSize", 4096u),
            ("L3CacheSize", 32768u),
            ("VirtualizationFirmwareEnabled", true)));

    private static StubRegistry RealRegistry() => new StubRegistry()
        .With("DisplayVersion", RegistryValueState.Text("25H2"))
        .With("UBR", RegistryValueState.DWord(9711));

    [Fact]
    public void ReadsTheOperatingSystemWithTheDisplayVersionAndRevision()
    {
        var os = new PlatformCollector(RealMachine(), RealRegistry(), "PC-DA-SALA").ReadOs();

        Assert.Equal("Windows 11 Home", os.Name);
        Assert.Equal("25H2", os.DisplayVersion);
        Assert.Equal("26200.9711", os.FullBuild);
        Assert.Equal("10.0.26200", os.Version);
        Assert.Equal("64 bits", os.Architecture);
        Assert.Equal("PC-DA-SALA", os.ComputerName);
    }

    [Fact]
    public void WithoutRegistryValuesTheBuildStaysPlain()
    {
        var os = new PlatformCollector(RealMachine(), new StubRegistry(), "PC").ReadOs();

        Assert.Null(os.DisplayVersion);
        Assert.Equal("26200", os.FullBuild);
    }

    [Fact]
    public void BoardPlaceholdersBecomeMissingAndTheSerialStaysHidden()
    {
        var board = new PlatformCollector(RealMachine(), RealRegistry(), "PC").ReadBoard();

        Assert.Equal("ASUSTeK COMPUTER INC.", board.Manufacturer);
        Assert.Equal("TUF GAMING B550M-PLUS", board.Model);
        Assert.False(board.SerialNumber.HasValue);
        Assert.Equal("—", board.SerialNumber.Display(reveal: true));
    }

    [Fact]
    public void ARealSerialIsSensitive()
    {
        var wmi = new FakeWmiReader().With("Win32_BaseBoard", WmiRow.Of(("Manufacturer", "MSI"), ("Product", "B650"), ("SerialNumber", "07E1234567")));

        var board = new PlatformCollector(wmi, RealRegistry(), "PC").ReadBoard();

        Assert.True(board.SerialNumber.HasValue);
        Assert.Equal(SensitiveMask(), board.SerialNumber.Display(reveal: false));
        Assert.Equal("07E1234567", board.SerialNumber.Display(reveal: true));
        Assert.DoesNotContain("07E1234567", board.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheBiosDate()
    {
        var bios = new PlatformCollector(RealMachine(), RealRegistry(), "PC").ReadBios();

        Assert.Equal("American Megatrends Inc.", bios.Manufacturer);
        Assert.Equal("3636", bios.Version);
        Assert.Equal(new DateOnly(2026, 1, 4), bios.ReleaseDate);
    }

    [Fact]
    public void TheBiosDateIgnoresTheUtcOffsetBecauseSmbiosDatesHaveNoTimeZone()
    {
        var wmi = new FakeWmiReader().With("Win32_BIOS", WmiRow.Of(("Manufacturer", "AMI"), ("SMBIOSBIOSVersion", "1"), ("ReleaseDate", "20260104000000.000000-180")));

        var bios = new PlatformCollector(wmi, RealRegistry(), "PC").ReadBios();

        Assert.Equal(new DateOnly(2026, 1, 4), bios.ReleaseDate);
    }

    [Fact]
    public void ReadsTheCpuAndNamesTheVendor()
    {
        var cpu = Assert.Single(new PlatformCollector(RealMachine(), RealRegistry(), "PC").ReadCpus());

        Assert.Equal("AMD Ryzen 7 5700X 8-Core Processor", cpu.Name);
        Assert.Equal("AMD", cpu.Manufacturer);
        Assert.Equal("AM4", cpu.Socket);
        Assert.Equal(8, cpu.Cores);
        Assert.Equal(16, cpu.Threads);
        Assert.Equal(3401, cpu.MaxClockMhz);
        Assert.Equal(4096, cpu.L2CacheKb);
        Assert.Equal(32768, cpu.L3CacheKb);
        Assert.True(cpu.VirtualizationEnabled);
    }

    [Theory]
    [InlineData("AuthenticAMD", "AMD")]
    [InlineData("GenuineIntel", "Intel")]
    [InlineData("genuineintel", "Intel")]
    [InlineData("Qualcomm", "Qualcomm")]
    [InlineData("", "—")]
    [InlineData(null, "—")]
    public void VendorNamesAreNormalized(string? raw, string expected)
    {
        Assert.Equal(expected, PlatformCollector.VendorName(raw));
    }

    [Theory]
    [InlineData("To Be Filled By O.E.M.", null)]
    [InlineData("Default string", null)]
    [InlineData("  None  ", null)]
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("  Rev X.0x ", "Rev X.0x")]
    public void FirmwarePlaceholdersAreDropped(string? raw, string? expected)
    {
        Assert.Equal(expected, PlatformCollector.Clean(raw));
    }

    [Fact]
    public void AnEmptyAnswerFromWindowsIsAnError()
    {
        var empty = new FakeWmiReader();
        var collector = new PlatformCollector(empty, RealRegistry(), "PC");

        Assert.Throws<HardwareReadException>(collector.ReadOs);
        Assert.Throws<HardwareReadException>(collector.ReadBoard);
        Assert.Throws<HardwareReadException>(collector.ReadBios);
        Assert.Throws<HardwareReadException>(collector.ReadCpus);
    }

    [Fact]
    public void QueriesAskOnlyForNamedPropertiesOfFixedClasses()
    {
        var wmi = RealMachine();
        var collector = new PlatformCollector(wmi, RealRegistry(), "PC");

        collector.ReadOs();
        collector.ReadBoard();
        collector.ReadBios();
        collector.ReadCpus();

        Assert.Equal(["Win32_OperatingSystem", "Win32_BaseBoard", "Win32_BIOS", "Win32_Processor"], wmi.Queries.Select(q => q.ClassName));
        Assert.All(wmi.Queries, q => Assert.DoesNotContain("*", q.ToWql(), StringComparison.Ordinal));
    }

    private static string SensitiveMask() => M1vClock.Core.Privacy.SensitiveText.Mask;
}

public sealed class PlatformServiceTests
{
    [Fact]
    public async Task OneFailingSectionDoesNotTakeTheOthersDown()
    {
        var wmi = new FakeWmiReader()
            .With("Win32_OperatingSystem", WmiRow.Of(("Caption", "Microsoft Windows 11 Home"), ("Version", "10.0.26200"), ("BuildNumber", "26200"), ("OSArchitecture", "64 bits")))
            .Failing("Win32_BaseBoard", new UnauthorizedAccessException("Acesso negado."))
            .With("Win32_BIOS", WmiRow.Of(("Manufacturer", "AMI"), ("SMBIOSBIOSVersion", "1")))
            .With("Win32_Processor", WmiRow.Of(("Name", "CPU"), ("Manufacturer", "GenuineIntel")));
        var logger = new CollectingLogger<PlatformService>();
        var service = new PlatformService(new PlatformCollector(wmi, new StubRegistry(), "PC"), logger);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Os.IsSuccess);
        Assert.True(report.Bios.IsSuccess);
        Assert.True(report.Cpus.IsSuccess);
        Assert.False(report.Board.IsSuccess);
        Assert.Equal("Acesso negado.", report.Board.Error);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(3001, entry.EventId.Id);
        Assert.IsType<UnauthorizedAccessException>(entry.Exception);
    }

    [Fact]
    public async Task WmiFailuresBecomeErrorsAndAreLogged()
    {
        var wmi = new FakeWmiReader()
            .Failing("Win32_OperatingSystem", new ManagementException("Classe inválida."))
            .Failing("Win32_BaseBoard", new TimeoutException("Demorou demais."))
            .Failing("Win32_BIOS", new InvalidOperationException("Serviço parado."))
            .Failing("Win32_Processor", new HardwareReadException("Nada."));
        var logger = new CollectingLogger<PlatformService>();
        var service = new PlatformService(new PlatformCollector(wmi, new StubRegistry(), "PC"), logger);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(report.Os.IsSuccess);
        Assert.False(report.Board.IsSuccess);
        Assert.False(report.Bios.IsSuccess);
        Assert.False(report.Cpus.IsSuccess);
        Assert.Equal(4, logger.Entries.Count);
    }

    [Fact]
    public async Task UnexpectedBugsAreNotSwallowed()
    {
        var wmi = new FakeWmiReader().Failing("Win32_OperatingSystem", new FormatException("bug"));
        var service = new PlatformService(new PlatformCollector(wmi, new StubRegistry(), "PC"), new CollectingLogger<PlatformService>());

        await Assert.ThrowsAsync<FormatException>(() => service.ReadAsync(TestContext.Current.CancellationToken));
    }
}
