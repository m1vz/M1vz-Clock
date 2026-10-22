using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Hardware;
using M1vClock.Benchmarks.Results;

namespace M1vClock.App.Pages.Performance;

public static class BenchNames
{
    public static string Test(string test) => Loc.Get($"Bench.TestName.{Key(test)}");

    public static string Metric(string metric) => Loc.Get($"Bench.Metric.{metric}");

    public static string Value(string test, double score)
    {
        var info = BenchmarkCatalog.Find(test);
        var decimals = info?.ScoreDecimals ?? 0;
        var unit = info?.ScoreUnit ?? string.Empty;
        return $"{score.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Loc.Culture)} {unit}".Trim();
    }

    public static string Metric(string test, string metric, double value)
    {
        var info = BenchmarkCatalog.FindMetric(test, metric);
        var decimals = info?.Decimals ?? 0;
        var unit = info?.Unit ?? string.Empty;
        return $"{value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Loc.Culture)} {unit}".Trim();
    }

    public static string Percent(double change)
    {
        var sign = change > 0 ? "+" : string.Empty;
        return $"{sign}{change.ToString("0.0", Loc.Culture)} %";
    }

    public static MetricState Tone(ComparisonVerdict verdict) => verdict switch
    {
        ComparisonVerdict.Better => MetricState.Good,
        ComparisonVerdict.Worse => MetricState.Critical,
        _ => MetricState.Neutral,
    };

    public static string VerdictText(ComparisonVerdict verdict) => Loc.Get($"Bench.Compare.Verdict.{verdict}");

    private static string Key(string test) => test.Replace('.', '_');
}

public sealed partial class BenchHistoryRowViewModel : ObservableObject
{
    public BenchHistoryRowViewModel(BenchmarkRecord record, double fraction, bool isBaseline)
    {
        ArgumentNullException.ThrowIfNull(record);
        Record = record;
        Fraction = fraction;
        IsBaseline = isBaseline;
    }

    public BenchmarkRecord Record { get; }

    public double Fraction { get; }

    public bool IsBaseline { get; }

    public string TestText => BenchNames.Test(Record.Test);

    public string DateText => Record.At.LocalDateTime.ToString("g", Loc.Culture);

    public string ScoreText => BenchNames.Value(Record.Test, Record.Score);

    public string MachineText => Record.Imported
        ? Loc.Format("Bench.History.Imported", string.IsNullOrWhiteSpace(Record.Machine.Cpu) ? Loc.Get("Bench.History.OtherPc") : Record.Machine.Cpu)
        : Loc.Get("Bench.History.ThisPc");

    public string ModeText => Record.Sustained ? Loc.Get("Bench.History.Sustained") : string.Empty;

    public bool HasMode => Record.Sustained;

    public bool IsImported => Record.Imported;

    public IRelayCommand<BenchHistoryRowViewModel>? SelectCommand { get; init; }

    public IRelayCommand<BenchHistoryRowViewModel>? DeleteCommand { get; init; }
}

public static class BenchComparisonRows
{
    public static InfoSectionViewModel Build(BenchmarkComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var title = Loc.Format(
            "Bench.Compare.Title",
            comparison.Baseline.At.LocalDateTime.ToString("g", Loc.Culture),
            comparison.Baseline.Imported ? Loc.Get("Bench.History.OtherPc") : Loc.Get("Bench.History.ThisPc"));
        var section = new InfoSectionViewModel(title, "Icon.Benchmarks");
        var test = comparison.Current.Test;
        if (comparison.Problem != ComparisonProblem.None)
        {
            section.StatusText = Loc.Get($"Bench.Compare.Problem.{comparison.Problem}");
            section.StatusTone = MetricState.Warning;
            section.SetReady([]);
            return section;
        }

        section.StatusText = $"{BenchNames.Percent(comparison.ScoreChangePercent)} · {BenchNames.VerdictText(comparison.ScoreVerdict)}";
        section.StatusTone = BenchNames.Tone(comparison.ScoreVerdict);
        var rows = new List<InfoRowViewModel>
        {
            new(Loc.Get("Bench.Compare.Score"), $"{BenchNames.Value(test, comparison.Baseline.Score)} → {BenchNames.Value(test, comparison.Current.Score)}"),
        };
        foreach (var metric in comparison.Metrics)
        {
            rows.Add(new InfoRowViewModel(
                BenchNames.Metric(metric.Key),
                $"{BenchNames.Metric(test, metric.Key, metric.Baseline)} → {BenchNames.Metric(test, metric.Key, metric.Current)} ({BenchNames.Percent(metric.ChangePercent)})"));
        }

        section.SetReady(rows);
        var notes = new List<string>();
        if (!comparison.SameMachine)
        {
            notes.Add(Loc.Get("Bench.Compare.OtherMachine"));
        }

        notes.Add(Loc.Format("Bench.Compare.NoiseNote", (int)BenchmarkComparer.NoisePercent));
        section.SetNotes(notes);
        return section;
    }
}
