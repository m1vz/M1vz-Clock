using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Health;
using M1vClock.Hardware.Sensors;
using M1vClock.Hardware.Storage;

namespace M1vClock.App.Pages.Hardware;

public sealed partial class OverviewCardViewModel(string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty]
    public partial string Value { get; set; } = Units.Missing;

    [ObservableProperty]
    public partial string Unit { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState State { get; set; }

    [ObservableProperty]
    public partial string StateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    public void Set(string value, string unit, string detail, MetricState state, string stateText)
    {
        Value = value;
        Unit = unit;
        Detail = detail;
        State = state;
        StateText = stateText;
        IsLoading = false;
    }
}

public sealed class HealthRowViewModel(string title, string detail, HealthStatus status)
{
    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public HealthStatus Status { get; } = status;

    public string IconKey { get; } = OverviewRows.HealthIcon(status);

    public MetricState Tone { get; } = OverviewRows.HealthTone(status);
}

public static class OverviewRows
{
    public const float CpuWarmCelsius = 75;
    public const float GpuWarmCelsius = 80;
    public const float HotCelsius = 90;

    public static MetricState CpuTemperatureState(float? celsius) => TemperatureState(celsius, CpuWarmCelsius);

    public static MetricState GpuTemperatureState(float? celsius) => TemperatureState(celsius, GpuWarmCelsius);

    public static MetricState MemoryState(float? usedPercent) => usedPercent switch
    {
        null => MetricState.Neutral,
        >= (float)HealthEvaluator.MemoryCriticalPercent => MetricState.Critical,
        >= (float)HealthEvaluator.MemoryAttentionPercent => MetricState.Warning,
        _ => MetricState.Good,
    };

    public static MetricState DiskState(double? freePercent) => freePercent switch
    {
        null => MetricState.Neutral,
        < HealthEvaluator.DriveCriticalFreePercent => MetricState.Critical,
        < HealthEvaluator.DriveAttentionFreePercent => MetricState.Warning,
        _ => MetricState.Good,
    };

    public static string TemperatureStateText(MetricState state) => state switch
    {
        MetricState.Good => Loc.Get("State.Normal"),
        MetricState.Warning => Loc.Get("State.Warm"),
        MetricState.Critical => Loc.Get("State.High"),
        _ => string.Empty,
    };

    public static string FillStateText(MetricState state) => state switch
    {
        MetricState.Good => Loc.Get("State.Normal"),
        MetricState.Warning => Loc.Get("State.High"),
        MetricState.Critical => Loc.Get("State.Full"),
        _ => string.Empty,
    };

    public static (string Value, string Unit) Temperature(float celsius, TemperatureUnit unit)
    {
        var text = SensorFormat.Value(SensorKind.Temperature, celsius, unit);
        var separator = text.LastIndexOf(' ');
        return (text[..separator], text[(separator + 1)..]);
    }

    public static void Processor(OverviewCardViewModel card, float? temperature, float? load, float? power, float? clock, TemperatureUnit unit, Func<float?, MetricState> state)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(state);
        var details = new List<string>();
        if (temperature is { } celsius)
        {
            AddIfPresent(details, SensorKind.Load, load);
            AddIfPresent(details, SensorKind.Power, power);
            AddIfPresent(details, SensorKind.Clock, clock);
            var (value, symbol) = Temperature(celsius, unit);
            var tone = state(celsius);
            card.Set(value, symbol, string.Join(" · ", details), tone, TemperatureStateText(tone));
            return;
        }

        AddIfPresent(details, SensorKind.Power, power);
        AddIfPresent(details, SensorKind.Clock, clock);
        if (load is { } percent)
        {
            card.Set(percent.ToString("0", Loc.Culture), "%", string.Join(" · ", details), MetricState.Neutral, string.Empty);
            return;
        }

        card.Set(Units.Missing, string.Empty, string.Join(" · ", details), MetricState.Neutral, string.Empty);
    }

    public static void Memory(OverviewCardViewModel card, SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(snapshot);
        var detail = snapshot is { MemoryUsedGb: { } used, MemoryAvailableGb: { } available }
            ? Loc.Format("Hw.Overview.Detail.MemoryOf", used.ToString("0.0", Loc.Culture), (used + available).ToString("0", Loc.Culture))
            : string.Empty;
        if (snapshot.MemoryLoad is not { } load)
        {
            card.Set(Units.Missing, string.Empty, detail, MetricState.Neutral, string.Empty);
            return;
        }

        var state = MemoryState(load);
        card.Set(load.ToString("0", Loc.Culture), "%", detail, state, FillStateText(state));
    }

    public static void Disk(OverviewCardViewModel card, Volume? volume)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (volume is not { TotalBytes: > 0 })
        {
            card.Set(Units.Missing, string.Empty, string.Empty, MetricState.Neutral, string.Empty);
            return;
        }

        var free = 100.0 * volume.FreeBytes / volume.TotalBytes;
        var state = DiskState(free);
        var detail = Loc.Format("Hw.Overview.Detail.DiskFree", Units.DecimalBytes(volume.FreeBytes), Units.DecimalBytes(volume.TotalBytes));
        card.Set((100 - free).ToString("0", Loc.Culture), "%", detail, state, FillStateText(state));
    }

    public static string HealthIcon(HealthStatus status) => status switch
    {
        HealthStatus.Ok => "Icon.Success",
        HealthStatus.Attention => "Icon.Warning",
        HealthStatus.Critical => "Icon.Error",
        _ => "Icon.Info",
    };

    public static MetricState HealthTone(HealthStatus status) => status switch
    {
        HealthStatus.Ok => MetricState.Good,
        HealthStatus.Attention => MetricState.Warning,
        HealthStatus.Critical => MetricState.Critical,
        _ => MetricState.Neutral,
    };

    public static string HealthOverall(HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Overall switch
        {
            HealthStatus.Ok => Loc.Get("Hw.Overview.Health.Ok"),
            HealthStatus.Attention => Loc.Format("Hw.Overview.Health.Attention", report.Problems.ToString(Loc.Culture)),
            HealthStatus.Critical => Loc.Format("Hw.Overview.Health.Critical", report.Problems.ToString(Loc.Culture)),
            _ => Loc.Get("Hw.Overview.Health.Unknown"),
        };
    }

    public static string HealthDetail(HealthCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var kind = check.Kind;
        if (kind == HealthKind.CpuSensors)
        {
            var problem = (CpuSensorProblem)(int)check.Value;
            return Loc.Get(problem == CpuSensorProblem.None ? "Hw.Health.CpuSensors.Ok" : $"Hw.Health.CpuSensors.{problem}");
        }

        var key = $"Hw.Health.{kind}.{check.Status}";
        return kind switch
        {
            HealthKind.Disks or HealthKind.Usb when check.Status is HealthStatus.Attention or HealthStatus.Critical => Loc.Format(key, ((int)check.Value).ToString(Loc.Culture)),
            HealthKind.Memory or HealthKind.SystemDrive => Loc.Format(key, check.Value.ToString("0", Loc.Culture)),
            _ => Loc.Get(key),
        };
    }

    public static IReadOnlyList<HealthRowViewModel> Health(HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return
        [
            .. report.Checks
                .OrderBy(c => Severity(c.Status))
                .Select(c => new HealthRowViewModel(Loc.Get($"Hw.Health.{c.Kind}.Title"), HealthDetail(c), c.Status)),
        ];
    }

    private static int Severity(HealthStatus status) => status switch
    {
        HealthStatus.Critical => 0,
        HealthStatus.Attention => 1,
        HealthStatus.Unknown => 2,
        _ => 3,
    };

    private static MetricState TemperatureState(float? celsius, float warm) => celsius switch
    {
        null => MetricState.Neutral,
        >= HotCelsius => MetricState.Critical,
        _ when celsius >= warm => MetricState.Warning,
        _ => MetricState.Good,
    };

    private static void AddIfPresent(List<string> details, SensorKind kind, float? value)
    {
        if (value is { } number && (kind == SensorKind.Load || number > 0))
        {
            details.Add(SensorFormat.Value(kind, number));
        }
    }
}
