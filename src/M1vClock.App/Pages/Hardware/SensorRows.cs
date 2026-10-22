using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Sensors;

namespace M1vClock.App.Pages.Hardware;

public enum SensorFilterKind
{
    All,
    Temperature,
    Load,
    Clock,
    Power,
    Fan,
    Other,
}

public sealed partial class SensorRowViewModel : ObservableObject
{
    public SensorRowViewModel(SensorReading reading, TemperatureUnit unit)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Id = reading.Id;
        Name = reading.Name;
        HardwareName = reading.HardwareName;
        Hardware = reading.Hardware;
        Kind = reading.Kind;
        Update(reading, unit);
    }

    public string Id { get; }

    public string Name { get; }

    public string HardwareName { get; }

    public SensorHardware Hardware { get; }

    public SensorKind Kind { get; }

    [ObservableProperty]
    public partial string Value { get; set; } = Units.Missing;

    [ObservableProperty]
    public partial string Min { get; set; } = Units.Missing;

    [ObservableProperty]
    public partial string Max { get; set; } = Units.Missing;

    public void Update(SensorReading reading, TemperatureUnit unit)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Value = SensorFormat.Value(Kind, reading.Value, unit);
        Min = SensorFormat.Value(Kind, reading.Min, unit);
        Max = SensorFormat.Value(Kind, reading.Max, unit);
    }
}

public sealed class SensorGroupViewModel(string title, string subtitle, string iconKey, IReadOnlyList<SensorRowViewModel> rows)
{
    public string Title { get; } = title;

    public string Subtitle { get; } = subtitle;

    public string IconKey { get; } = iconKey;

    public IReadOnlyList<SensorRowViewModel> Rows { get; } = rows;
}

public static class SensorRows
{
    public static string HardwareLabel(SensorHardware hardware) => Loc.Get($"Hw.Sensor.Hardware.{hardware}");

    public static string FilterLabel(SensorFilterKind filter) => Loc.Get($"Hw.Sensor.Filter.{filter}");

    public static string IconFor(SensorHardware hardware) => hardware switch
    {
        SensorHardware.Cpu => "Icon.Cpu",
        SensorHardware.Gpu => "Icon.Gpu",
        SensorHardware.Memory => "Icon.Memory",
        SensorHardware.Motherboard => "Icon.Board",
        SensorHardware.Storage => "Icon.Storage",
        SensorHardware.Network => "Icon.Network",
        SensorHardware.PowerSupply or SensorHardware.Battery => "Icon.Power",
        _ => "Icon.Sensors",
    };

    public static int KindOrder(SensorKind kind) => kind switch
    {
        SensorKind.Clock => 0,
        SensorKind.Temperature => 1,
        SensorKind.Load => 2,
        SensorKind.Fan => 3,
        SensorKind.Control => 4,
        SensorKind.Power => 5,
        SensorKind.Voltage => 6,
        SensorKind.Current => 7,
        SensorKind.SmallData => 8,
        SensorKind.Data => 9,
        SensorKind.Throughput => 10,
        _ => 11,
    };

    public static SensorFilterKind FilterOf(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => SensorFilterKind.Temperature,
        SensorKind.Load => SensorFilterKind.Load,
        SensorKind.Clock => SensorFilterKind.Clock,
        SensorKind.Power => SensorFilterKind.Power,
        SensorKind.Fan => SensorFilterKind.Fan,
        _ => SensorFilterKind.Other,
    };

    public static bool Matches(SensorRowViewModel row, string query, SensorFilterKind filter)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(query);
        if (filter != SensorFilterKind.All && FilterOf(row.Kind) != filter)
        {
            return false;
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term =>
            row.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || row.HardwareName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || HardwareLabel(row.Hardware).Contains(term, StringComparison.CurrentCultureIgnoreCase));
    }

    public static IReadOnlyList<SensorGroupViewModel> Group(IEnumerable<SensorRowViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return
        [
            .. rows
                .GroupBy(r => (r.Hardware, r.HardwareName))
                .OrderBy(g => g.Key.Hardware)
                .ThenBy(g => g.Key.HardwareName, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new SensorGroupViewModel(
                    g.Key.HardwareName,
                    HardwareLabel(g.Key.Hardware),
                    IconFor(g.Key.Hardware),
                    [.. g.OrderBy(r => KindOrder(r.Kind)).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)])),
        ];
    }
}
