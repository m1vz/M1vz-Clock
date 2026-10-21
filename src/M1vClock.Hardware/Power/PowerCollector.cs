using System.Management;
using System.Runtime.InteropServices;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Power;

public sealed class PowerCollector(IWmiReader wmi, ILogger<PowerCollector> logger)
{
    private const string WmiScope = @"root\WMI";
    private const long UnknownRuntimeMinutes = 71_582_788;
    private const double MilliToUnit = 1000.0;

    private static readonly string[] BatteryProperties = ["Name", "EstimatedChargeRemaining", "BatteryStatus", "EstimatedRunTime", "Chemistry"];
    private static readonly string[] StaticProperties = ["DesignedCapacity", "ManufactureName", "DeviceName"];
    private static readonly string[] FullProperties = ["FullChargedCapacity"];
    private static readonly string[] CycleProperties = ["CycleCount"];
    private static readonly string[] LiveProperties = ["PowerOnline", "Charging", "Discharging", "Voltage", "ChargeRate", "DischargeRate"];

    public BatterySet ReadBatteries()
    {
        var windows = ReadOptional("Win32_Battery", BatteryProperties, WmiQuery.DefaultScope);
        var statics = ReadOptional("BatteryStaticData", StaticProperties, WmiScope);
        var full = ReadOptional("BatteryFullChargedCapacity", FullProperties, WmiScope);
        var cycles = ReadOptional("BatteryCycleCount", CycleProperties, WmiScope);
        var live = ReadOptional("BatteryStatus", LiveProperties, WmiScope);
        var count = Math.Max(windows.Count, statics.Count);
        var batteries = new List<BatteryInfo>();
        var onMains = false;
        for (var i = 0; i < count; i++)
        {
            var battery = Build(i, At(windows, i), At(statics, i), At(full, i), At(cycles, i), At(live, i));
            batteries.Add(battery);
            onMains |= IsOnMains(battery, At(windows, i));
        }

        return new BatterySet(batteries, onMains);
    }

    public static BatteryChemistry ChemistryOf(int code) => code switch
    {
        3 => BatteryChemistry.LeadAcid,
        4 => BatteryChemistry.NiCd,
        5 => BatteryChemistry.NiMh,
        6 => BatteryChemistry.LithiumIon,
        7 => BatteryChemistry.ZincAir,
        8 => BatteryChemistry.LithiumPolymer,
        _ => BatteryChemistry.Unknown,
    };

    public static BatteryState StateOf(int code) => code switch
    {
        1 => BatteryState.Discharging,
        2 => BatteryState.OnAc,
        3 => BatteryState.FullyCharged,
        4 => BatteryState.Low,
        5 => BatteryState.Critical,
        6 or 7 or 8 or 9 => BatteryState.Charging,
        11 => BatteryState.PartlyCharged,
        _ => BatteryState.Unknown,
    };

    public static bool IsOnMains(BatteryInfo battery, WmiRow? windows)
    {
        ArgumentNullException.ThrowIfNull(battery);
        if (battery.PowerOnline == true)
        {
            return true;
        }

        return battery.PowerOnline is null && windows?.Number("BatteryStatus") is 2 or 3 or 6 or 7 or 8 or 9;
    }

    private static WmiRow? At(IReadOnlyList<WmiRow> rows, int index) => index < rows.Count ? rows[index] : null;

    private static BatteryInfo Build(int index, WmiRow? windows, WmiRow? statics, WmiRow? full, WmiRow? cycles, WmiRow? live)
    {
        var design = statics?.Whole("DesignedCapacity");
        var fullCapacity = full?.Whole("FullChargedCapacity");
        var cycleCount = cycles?.Number("CycleCount");
        var minutes = windows?.Whole("EstimatedRunTime");
        var voltage = live?.Whole("Voltage");
        var chargeRate = live?.Whole("ChargeRate");
        var dischargeRate = live?.Whole("DischargeRate");
        var percent = windows?.Whole("EstimatedChargeRemaining");
        return new BatteryInfo(
            index,
            PlatformCollector.Clean(statics?.Text("ManufactureName")),
            PlatformCollector.Clean(statics?.Text("DeviceName")) ?? PlatformCollector.Clean(windows?.Text("Name")),
            ChemistryOf(windows?.Number("Chemistry") ?? 0),
            StateOf(windows?.Number("BatteryStatus") ?? 0),
            percent is >= 0 and <= 100 ? (int)percent : null,
            minutes is > 0 and < UnknownRuntimeMinutes ? TimeSpan.FromMinutes(minutes.Value) : null,
            design is > 0 ? design / MilliToUnit : null,
            fullCapacity is > 0 ? fullCapacity / MilliToUnit : null,
            cycleCount > 0 ? cycleCount : null,
            voltage is > 0 ? voltage / MilliToUnit : null,
            chargeRate is > 0 ? chargeRate / MilliToUnit : null,
            dischargeRate is > 0 ? dischargeRate / MilliToUnit : null,
            live?.Bool("PowerOnline"),
            live?.Bool("Charging") == true,
            live?.Bool("Discharging") == true);
    }

    private IReadOnlyList<WmiRow> ReadOptional(string className, string[] properties, string scope)
    {
        try
        {
            return wmi.Query(new WmiQuery(className, properties, scope));
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.InvalidNamespace or ManagementStatus.NotFound)
        {
            HardwareLog.SourceMissing(logger, className);
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            HardwareLog.OptionalSourceDenied(logger, className);
            return [];
        }
        catch (Exception ex) when (ex is ManagementException or COMException or InvalidOperationException or TimeoutException)
        {
            HardwareLog.OptionalSourceFailed(logger, ex, className);
            return [];
        }
    }
}
