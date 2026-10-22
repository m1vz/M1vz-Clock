using LibreHardwareMonitor.Hardware;
using M1vClock.Core.RegistryValues;
using M1vClock.Hardware.Sensors;

namespace M1vClock.Hardware.Tests;

public sealed class SensorMappingTests
{
    [Fact]
    public void EveryLibreHardwareMonitorSensorTypeMapsToAKind()
    {
        var unmapped = Enum.GetValues<SensorType>().Where(type => LibreHardwareSensorSource.MapKind(type) == SensorKind.Other).ToList();

        Assert.True(unmapped.Count == 0, "Tipos de sensor sem mapeamento: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void EveryLibreHardwareMonitorHardwareTypeMapsToACategory()
    {
        var unmapped = Enum.GetValues<HardwareType>().Where(type => LibreHardwareSensorSource.MapHardware(type) == SensorHardware.Other).ToList();

        Assert.True(unmapped.Count == 0, "Tipos de hardware sem mapeamento: " + string.Join(", ", unmapped));
    }

    [Theory]
    [InlineData(HardwareType.GpuNvidia, SensorHardware.Gpu)]
    [InlineData(HardwareType.GpuAmd, SensorHardware.Gpu)]
    [InlineData(HardwareType.GpuIntel, SensorHardware.Gpu)]
    [InlineData(HardwareType.SuperIO, SensorHardware.Motherboard)]
    [InlineData(HardwareType.Cooler, SensorHardware.Cooling)]
    [InlineData(HardwareType.Psu, SensorHardware.PowerSupply)]
    [InlineData(HardwareType.PowerMonitor, SensorHardware.PowerSupply)]
    public void SomeHardwareTypesShareACategory(HardwareType type, SensorHardware expected)
    {
        Assert.Equal(expected, LibreHardwareSensorSource.MapHardware(type));
    }

    [Theory]
    [InlineData(SensorType.Data, SensorKind.Data)]
    [InlineData(SensorType.SmallData, SensorKind.SmallData)]
    [InlineData(SensorType.Throughput, SensorKind.Throughput)]
    [InlineData(SensorType.Voltage, SensorKind.Voltage)]
    [InlineData(SensorType.TimeSpan, SensorKind.Duration)]
    [InlineData(SensorType.Timing, SensorKind.Duration)]
    [InlineData(SensorType.Noise, SensorKind.Noise)]
    [InlineData(SensorType.Conductivity, SensorKind.Conductivity)]
    public void KindsKeepTheirMeaning(SensorType type, SensorKind expected)
    {
        Assert.Equal(expected, LibreHardwareSensorSource.MapKind(type));
    }
}

public sealed class CpuSensorDiagnosticsTests
{
    private static SensorSnapshot WithCpuTemperature() =>
        new(DateTimeOffset.UnixEpoch, [FakeSensorSource.Reading("cpu/temp", SensorHardware.Cpu, "CPU Package", SensorKind.Temperature, 50)]);

    private static SensorSnapshot WithoutCpuTemperature() =>
        new(DateTimeOffset.UnixEpoch, [FakeSensorSource.Reading("cpu/load", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 10)]);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void WhenTheTemperatureIsThereThereIsNoProblemWhateverTheOtherFlagsSay(bool elevated, bool pawnIo)
    {
        Assert.Equal(CpuSensorProblem.None, CpuSensorDiagnostics.Diagnose(WithCpuTemperature(), elevated, pawnIo));
    }

    [Fact]
    public void WithoutAdministratorThatIsTheFirstSuspect()
    {
        Assert.Equal(CpuSensorProblem.NotElevated, CpuSensorDiagnostics.Diagnose(WithoutCpuTemperature(), elevated: false, pawnIoInstalled: false));
        Assert.Equal(CpuSensorProblem.NotElevated, CpuSensorDiagnostics.Diagnose(null, elevated: false, pawnIoInstalled: true));
    }

    [Fact]
    public void ElevatedWithoutThePawnIoDriverTheDriverIsMissing()
    {
        Assert.Equal(CpuSensorProblem.PawnIoMissing, CpuSensorDiagnostics.Diagnose(WithoutCpuTemperature(), elevated: true, pawnIoInstalled: false));
    }

    [Fact]
    public void ElevatedWithTheDriverButNoReadingTheReadFailed()
    {
        Assert.Equal(CpuSensorProblem.ReadFailed, CpuSensorDiagnostics.Diagnose(WithoutCpuTemperature(), elevated: true, pawnIoInstalled: true));
    }
}

public sealed class PawnIoProbeTests
{
    [Fact]
    public void ThePawnIoUninstallKeyMeansItIsInstalled()
    {
        var registry = new StubRegistry().With("DisplayName", RegistryValueState.Text("PawnIO"));

        Assert.True(new RegistryPawnIoProbe(registry).IsInstalled);
    }

    [Fact]
    public void WithoutTheKeyItIsNotInstalled()
    {
        var registry = new StubRegistry().With("DisplayName", RegistryValueState.Absent(keyExists: false));

        Assert.False(new RegistryPawnIoProbe(registry).IsInstalled);
    }

    [Fact]
    public void TheProbeLooksAtTheSixtyFourBitUninstallKeyOnly()
    {
        Assert.Equal(Microsoft.Win32.RegistryView.Registry64, RegistryPawnIoProbe.UninstallEntry.View);
        Assert.Equal(Microsoft.Win32.RegistryHive.LocalMachine, RegistryPawnIoProbe.UninstallEntry.Hive);
        Assert.EndsWith("Uninstall\\PawnIO", RegistryPawnIoProbe.UninstallEntry.KeyPath, StringComparison.Ordinal);
    }

    [Fact]
    public void ThisMachinesPawnIoStateCanBeRead()
    {
        var installed = new RegistryPawnIoProbe(new WindowsRegistryAccess()).IsInstalled;

        Assert.IsType<bool>(installed);
    }
}

public sealed class LibreHardwareSensorSourceTests
{
    [Fact]
    public void ReadingBeforeOpeningIsRefused()
    {
        using var source = new LibreHardwareSensorSource();

        Assert.Throws<InvalidOperationException>(() => source.Read());
    }

    [Fact]
    public void ThisMachineOpensAndReadsSensorsWithoutAdministrator()
    {
        using var source = new LibreHardwareSensorSource();

        source.Open();
        var readings = source.Read();

        Assert.NotEmpty(readings);
        Assert.Contains(readings, r => r.Hardware == SensorHardware.Cpu && r.Kind == SensorKind.Load);
        Assert.Contains(readings, r => r.Hardware == SensorHardware.Memory);
        Assert.All(readings, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Id));
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
        });
        Assert.Equal(readings.Count, readings.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ClosingTwiceIsHarmless()
    {
        var source = new LibreHardwareSensorSource();
        source.Open();

        source.Dispose();
        source.Dispose();

        Assert.Throws<InvalidOperationException>(() => source.Read());
    }
}
