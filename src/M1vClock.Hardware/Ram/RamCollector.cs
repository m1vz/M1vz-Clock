using M1vClock.Core.Privacy;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Wmi;

namespace M1vClock.Hardware.Ram;

public sealed class RamCollector(IWmiReader wmi)
{
    private const ulong BytesPerKilobyte = 1024;

    private static readonly string[] ModuleProperties =
    [
        "BankLabel", "DeviceLocator", "Capacity", "SMBIOSMemoryType", "FormFactor", "Speed", "ConfiguredClockSpeed",
        "Manufacturer", "PartNumber", "SerialNumber", "DataWidth", "TotalWidth", "Attributes",
        "ConfiguredVoltage", "MinVoltage", "MaxVoltage",
    ];

    private static readonly string[] ArrayProperties = ["MemoryDevices", "MaxCapacity", "MaxCapacityEx"];

    private static readonly string[] UsageProperties =
    [
        "TotalVisibleMemorySize", "FreePhysicalMemory", "TotalVirtualMemorySize", "FreeVirtualMemory",
    ];

    public RamInventory ReadInventory()
    {
        var modules = wmi.Query(new WmiQuery("Win32_PhysicalMemory", ModuleProperties)).Select(ToModule).ToList();
        if (modules.Count == 0)
        {
            throw new HardwareReadException("O Windows não informou nenhum módulo de memória.");
        }

        return new RamInventory(RamAnalyzer.Summarize(modules, ReadBoard()), modules);
    }

    public RamUsage ReadUsage()
    {
        var rows = wmi.Query(new WmiQuery("Win32_OperatingSystem", UsageProperties));
        if (rows.Count == 0)
        {
            throw new HardwareReadException("O Windows não informou o uso de memória.");
        }

        var row = rows[0];
        return new RamUsage(
            row.Size("TotalVisibleMemorySize") * BytesPerKilobyte,
            row.Size("FreePhysicalMemory") * BytesPerKilobyte,
            row.Size("TotalVirtualMemorySize") * BytesPerKilobyte,
            row.Size("FreeVirtualMemory") * BytesPerKilobyte);
    }

    private static RamModule ToModule(WmiRow row) => new(
        PlatformCollector.Clean(row.Text("BankLabel")) ?? string.Empty,
        PlatformCollector.Clean(row.Text("DeviceLocator")) ?? string.Empty,
        row.Size("Capacity"),
        RamAnalyzer.TypeOf(row.Number("SMBIOSMemoryType")),
        RamAnalyzer.FormFactorOf(row.Number("FormFactor")),
        row.Number("Speed"),
        row.Number("ConfiguredClockSpeed"),
        PlatformCollector.Clean(row.Text("Manufacturer")) ?? string.Empty,
        PlatformCollector.Clean(row.Text("PartNumber")) ?? string.Empty,
        new SensitiveText(PlatformCollector.Clean(row.Text("SerialNumber"))),
        row.Number("DataWidth"),
        row.Number("TotalWidth"),
        RamAnalyzer.RanksOf(row.Number("Attributes")),
        RamAnalyzer.VoltsOf(row.Whole("ConfiguredVoltage")),
        RamAnalyzer.VoltsOf(row.Whole("MinVoltage")),
        RamAnalyzer.VoltsOf(row.Whole("MaxVoltage")));

    private RamBoard ReadBoard()
    {
        var slots = 0;
        ulong maxBytes = 0;
        foreach (var row in wmi.Query(new WmiQuery("Win32_PhysicalMemoryArray", ArrayProperties)))
        {
            slots += row.Number("MemoryDevices");
            var kilobytes = row.Raw("MaxCapacityEx") is null ? row.Size("MaxCapacity") : row.Size("MaxCapacityEx");
            maxBytes += kilobytes * BytesPerKilobyte;
        }

        return new RamBoard(slots, maxBytes);
    }
}
