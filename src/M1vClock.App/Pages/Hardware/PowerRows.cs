using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Hardware.Power;

namespace M1vClock.App.Pages.Hardware;

public static class PowerRows
{
    public static string Verdict(BatteryVerdict verdict) => Loc.Get($"Hw.Power.Verdict.{verdict}");

    public static MetricState Tone(BatteryVerdict verdict) => verdict switch
    {
        BatteryVerdict.Good => MetricState.Good,
        BatteryVerdict.Warning => MetricState.Warning,
        BatteryVerdict.Worn => MetricState.Critical,
        _ => MetricState.Neutral,
    };

    public static string Finding(BatteryFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var key = $"Hw.Power.Finding.{finding.Kind}";
        return finding.Kind switch
        {
            BatteryFindingKind.NoCapacityData or BatteryFindingKind.LowCharge => Loc.Get(key),
            BatteryFindingKind.ManyCycles => Loc.Format(key, finding.Value.ToString("N0", Loc.Culture)),
            _ => Loc.Format(key, finding.Value.ToString("0", Loc.Culture)),
        };
    }

    public static IReadOnlyList<string> Notes(BatteryAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        return assessment.Findings.Select(Finding).ToList();
    }

    public static string Title(BatteryInfo battery, int total)
    {
        ArgumentNullException.ThrowIfNull(battery);
        return total > 1 ? Loc.Format("Hw.Power.Battery.N", battery.Index + 1) : Loc.Get("Hw.Power.Battery");
    }

    public static string Subtitle(BatteryInfo battery)
    {
        ArgumentNullException.ThrowIfNull(battery);
        var charge = battery.ChargePercent is { } percent ? Loc.Format("Hw.Power.Battery.Charge", percent.ToString(Loc.Culture)) : string.Empty;
        return string.Join(" · ", new[] { battery.Manufacturer ?? string.Empty, charge }.Where(p => p.Length > 0));
    }

    public static IReadOnlyList<InfoRowViewModel> Metrics(BatteryInfo battery, bool onMains)
    {
        ArgumentNullException.ThrowIfNull(battery);
        var rows = new List<InfoRowViewModel>();
        if (battery.ChargePercent is { } percent)
        {
            rows.Add(Row("Hw.Power.Metric.Charge", $"{percent.ToString(Loc.Culture)}%"));
        }

        var state = StateText(battery);
        if (state is not null)
        {
            rows.Add(Row("Hw.Power.Metric.State", state));
        }

        if (battery.EstimatedRuntime is { } runtime && !onMains)
        {
            rows.Add(Row("Hw.Power.Metric.Runtime", Loc.Format("Hw.Power.Metric.Runtime.Value", ((int)runtime.TotalHours).ToString(Loc.Culture), runtime.Minutes.ToString("00", Loc.Culture))));
        }

        AddWattHours(rows, "Hw.Power.Metric.Design", battery.DesignWattHours);
        AddWattHours(rows, "Hw.Power.Metric.Full", battery.FullWattHours);
        if (battery.WearPercent is { } wear)
        {
            rows.Add(Row("Hw.Power.Metric.Wear", $"{wear.ToString("0.0", Loc.Culture)}%"));
        }

        if (battery.Cycles is { } cycles)
        {
            rows.Add(Row("Hw.Power.Metric.Cycles", cycles.ToString("N0", Loc.Culture)));
        }

        if (battery.Volts is { } volts)
        {
            rows.Add(Row("Hw.Power.Metric.Voltage", string.Create(Loc.Culture, $"{volts:0.00} V")));
        }

        if (battery is { Charging: true, ChargeWatts: { } charge })
        {
            rows.Add(Row("Hw.Power.Metric.ChargePower", Watts(charge)));
        }

        if (battery is { Discharging: true, DischargeWatts: { } draw })
        {
            rows.Add(Row("Hw.Power.Metric.DischargePower", Watts(draw)));
        }

        AddText(rows, "Hw.Power.Metric.Maker", battery.Manufacturer);
        AddText(rows, "Hw.Power.Metric.Model", battery.Model);
        if (battery.Chemistry != BatteryChemistry.Unknown)
        {
            rows.Add(Row("Hw.Power.Metric.Chemistry", Loc.Get($"Hw.Power.Chem.{battery.Chemistry}")));
        }

        return rows;
    }

    public static IReadOnlyList<InfoRowViewModel> Energy(PowerSource source, PowerPlan? plan)
    {
        var rows = new List<InfoRowViewModel> { Row("Hw.Power.Energy.Source", Loc.Get($"Hw.Power.Source.{source}")) };
        if (plan is not null)
        {
            rows.Add(Row("Hw.Power.Energy.Plan", PlanText(plan)));
        }

        return rows;
    }

    public static IReadOnlyList<string> EnergyNotes(PowerSource source, PowerPlan? plan)
    {
        var notes = new List<string>();
        if (source == PowerSource.DesktopMains)
        {
            notes.Add(Loc.Get("Hw.Power.Note.NoBattery"));
        }

        if (plan is null)
        {
            notes.Add(Loc.Get("Hw.Power.Note.PlanUnknown"));
        }
        else if (plan.IsKnownAndSlow)
        {
            notes.Add(Loc.Get("Hw.Power.Note.PlanSlow"));
        }

        if (source == PowerSource.DesktopMains)
        {
            notes.Add(Loc.Get("Hw.Power.Note.Psu"));
        }

        return notes;
    }

    public static string PlanText(PowerPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Kind == PowerPlanKind.Custom ? Loc.Format("Hw.Power.Plan.Custom", plan.Name) : plan.Name;
    }

    public static string? StateText(BatteryInfo battery)
    {
        ArgumentNullException.ThrowIfNull(battery);
        if (battery.State != BatteryState.Unknown)
        {
            return Loc.Get($"Hw.Power.State.{battery.State}");
        }

        if (battery.Charging)
        {
            return Loc.Get("Hw.Power.State.Charging");
        }

        return battery.Discharging ? Loc.Get("Hw.Power.State.Discharging") : null;
    }

    private static void AddWattHours(List<InfoRowViewModel> rows, string key, double? value)
    {
        if (value is { } wattHours)
        {
            rows.Add(Row(key, Loc.Format("Hw.Power.Metric.Wh", wattHours.ToString("0.0", Loc.Culture))));
        }
    }

    private static void AddText(List<InfoRowViewModel> rows, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(Row(key, value));
        }
    }

    private static string Watts(double watts) => Loc.Format("Hw.Power.Metric.Watts", watts.ToString("0.0", Loc.Culture));

    private static InfoRowViewModel Row(string labelKey, string value) => new(Loc.Get(labelKey), value);
}
