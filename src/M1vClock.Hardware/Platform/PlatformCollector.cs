using System.Globalization;
using M1vClock.Core.Privacy;
using M1vClock.Core.RegistryValues;
using M1vClock.Hardware.Wmi;
using Microsoft.Win32;

namespace M1vClock.Hardware.Platform;

public sealed class PlatformCollector(IWmiReader wmi, IRegistryAccess registry, string computerName)
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private static readonly string[] Placeholders =
    [
        "to be filled by o.e.m.", "to be filled by oem", "default string", "not specified", "not applicable", "none", "n/a",
        "system serial number", "system product name", "system version", "base board serial number", "0", "unknown",
    ];

    private static readonly string[] OsProperties = ["Caption", "Version", "BuildNumber", "OSArchitecture"];
    private static readonly string[] BoardProperties = ["Manufacturer", "Product", "Version", "SerialNumber"];
    private static readonly string[] BiosProperties = ["Manufacturer", "SMBIOSBIOSVersion", "ReleaseDate"];

    private static readonly string[] CpuProperties =
    [
        "Name", "Manufacturer", "SocketDesignation", "NumberOfCores", "NumberOfLogicalProcessors",
        "MaxClockSpeed", "L2CacheSize", "L3CacheSize", "VirtualizationFirmwareEnabled",
    ];

    public OsInfo ReadOs()
    {
        var row = First("Win32_OperatingSystem", OsProperties);
        var revision = registry.Read(new RegistryValueAddress(RegistryHive.LocalMachine, RegistryView.Registry64, CurrentVersionKey, "UBR"));
        var display = registry.Read(new RegistryValueAddress(RegistryHive.LocalMachine, RegistryView.Registry64, CurrentVersionKey, "DisplayVersion"));
        return new OsInfo(
            StripVendor(row.Text("Caption") ?? "Windows"),
            row.Text("Version") ?? "—",
            row.Text("BuildNumber") ?? "—",
            display.ValueExists ? Clean(Convert.ToString(display.Data, CultureInfo.InvariantCulture)) : null,
            revision.ValueExists ? Convert.ToString(revision.Data, CultureInfo.InvariantCulture) : null,
            row.Text("OSArchitecture") ?? "—",
            computerName);
    }

    public BoardInfo ReadBoard()
    {
        var row = First("Win32_BaseBoard", BoardProperties);
        return new BoardInfo(
            Clean(row.Text("Manufacturer")) ?? "—",
            Clean(row.Text("Product")) ?? "—",
            Clean(row.Text("Version")),
            new SensitiveText(Clean(row.Text("SerialNumber"))));
    }

    public BiosInfo ReadBios()
    {
        var row = First("Win32_BIOS", BiosProperties);
        var date = row.Date("ReleaseDate");
        return new BiosInfo(
            Clean(row.Text("Manufacturer")) ?? "—",
            Clean(row.Text("SMBIOSBIOSVersion")) ?? "—",
            date is null ? null : DateOnly.FromDateTime(date.Value));
    }

    public IReadOnlyList<CpuInfo> ReadCpus()
    {
        var rows = wmi.Query(new WmiQuery("Win32_Processor", CpuProperties));
        if (rows.Count == 0)
        {
            throw new HardwareReadException("O Windows não informou nenhum processador.");
        }

        return rows.Select(row => new CpuInfo(
            row.Text("Name") ?? "—",
            VendorName(row.Text("Manufacturer")),
            Clean(row.Text("SocketDesignation")),
            row.Number("NumberOfCores"),
            row.Number("NumberOfLogicalProcessors"),
            row.Number("MaxClockSpeed"),
            row.Number("L2CacheSize"),
            row.Number("L3CacheSize"),
            row.Bool("VirtualizationFirmwareEnabled"))).ToList();
    }

    public static string? Clean(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) || Placeholders.Contains(text, StringComparer.OrdinalIgnoreCase) ? null : text;
    }

    public static string VendorName(string? manufacturer) => manufacturer?.Trim() switch
    {
        null or "" => "—",
        var m when m.Equals("AuthenticAMD", StringComparison.OrdinalIgnoreCase) => "AMD",
        var m when m.Equals("GenuineIntel", StringComparison.OrdinalIgnoreCase) => "Intel",
        var m => m,
    };

    private static string StripVendor(string caption) =>
        caption.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) ? caption["Microsoft ".Length..] : caption;

    private WmiRow First(string className, string[] properties)
    {
        var rows = wmi.Query(new WmiQuery(className, properties));
        return rows.Count > 0 ? rows[0] : throw new HardwareReadException($"O Windows não retornou dados de {className}.");
    }
}
