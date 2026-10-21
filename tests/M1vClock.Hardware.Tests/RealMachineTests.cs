using M1vClock.Core.RegistryValues;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class RealMachineTests
{
    private static readonly string[] OsProperties = ["Caption", "BuildNumber"];

    [Fact]
    public void TheWindowsWmiReaderReturnsRowsWithTypedValues()
    {
        var rows = new WindowsWmiReader().Query(new WmiQuery("Win32_OperatingSystem", OsProperties));

        var row = Assert.Single(rows);
        Assert.Contains("Windows", row.Text("Caption"), StringComparison.Ordinal);
        Assert.True(row.Number("BuildNumber") >= 19045);
    }

    [Fact]
    public async Task ThePlatformServiceReadsThisMachine()
    {
        var collector = new PlatformCollector(new WindowsWmiReader(), new WindowsRegistryAccess(), Environment.MachineName);
        var service = new PlatformService(collector, NullLogger<PlatformService>.Instance);

        var report = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(report.Os.TryGet(out var os), report.Os.Error);
        Assert.Contains("Windows", os.Name, StringComparison.Ordinal);
        Assert.Equal(Environment.MachineName, os.ComputerName);
        Assert.True(report.Cpus.TryGet(out var cpus), report.Cpus.Error);
        var cpu = Assert.Single(cpus);
        Assert.True(cpu.Cores > 0);
        Assert.True(cpu.Threads >= cpu.Cores);
        Assert.True(cpu.MaxClockMhz > 500);
        Assert.True(report.Bios.IsSuccess, report.Bios.Error);
        Assert.True(report.Board.IsSuccess, report.Board.Error);
    }

    [Fact]
    public void ADisallowedClassFailsCleanlyInsteadOfCrashing()
    {
        var reader = new WindowsWmiReader();

        Assert.ThrowsAny<System.Management.ManagementException>(() => reader.Query(new WmiQuery("Win32_ClasseQueNaoExiste", OsProperties)));
    }
}
