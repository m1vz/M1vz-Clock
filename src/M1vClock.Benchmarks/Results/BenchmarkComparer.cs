using M1vClock.Benchmarks.Statistics;

namespace M1vClock.Benchmarks.Results;

public enum ComparisonVerdict
{
    WithinNoise,
    Better,
    Worse,
}

public enum ComparisonProblem
{
    None,
    DifferentTests,
    DifferentScoreVersions,
}

public sealed record MetricComparison(
    string Key,
    string Unit,
    int Decimals,
    double Baseline,
    double Current,
    double ChangePercent,
    ComparisonVerdict Verdict);

public sealed record BenchmarkComparison(
    ComparisonProblem Problem,
    BenchmarkRecord Baseline,
    BenchmarkRecord Current,
    double ScoreChangePercent,
    ComparisonVerdict ScoreVerdict,
    IReadOnlyList<MetricComparison> Metrics,
    bool SameMachine);

public static class BenchmarkComparer
{
    public const double NoisePercent = 3;

    public static BenchmarkComparison Compare(BenchmarkRecord baseline, BenchmarkRecord current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var sameMachine = baseline.Machine == current.Machine
            || (baseline.Machine.Cpu == current.Machine.Cpu
                && baseline.Machine.Gpu == current.Machine.Gpu
                && baseline.Machine.LogicalProcessors == current.Machine.LogicalProcessors
                && Math.Abs(baseline.Machine.MemoryGigabytes - current.Machine.MemoryGigabytes) < 0.5);

        if (!string.Equals(baseline.Test, current.Test, StringComparison.Ordinal))
        {
            return Unusable(ComparisonProblem.DifferentTests, baseline, current, sameMachine);
        }

        if (baseline.ScoreVersion != current.ScoreVersion)
        {
            return Unusable(ComparisonProblem.DifferentScoreVersions, baseline, current, sameMachine);
        }

        var info = BenchmarkCatalog.Find(baseline.Test);
        var higherIsBetter = info?.HigherIsBetter ?? true;
        var scoreChange = BenchStatistics.ChangePercent(baseline.Score, current.Score);
        var metrics = new List<MetricComparison>();
        if (info is not null)
        {
            foreach (var metric in info.Metrics)
            {
                if (baseline.Metrics.TryGetValue(metric.Key, out var before) && current.Metrics.TryGetValue(metric.Key, out var after))
                {
                    var change = BenchStatistics.ChangePercent(before, after);
                    metrics.Add(new MetricComparison(metric.Key, metric.Unit, metric.Decimals, before, after, change, Judge(change, metric.HigherIsBetter)));
                }
            }
        }

        return new BenchmarkComparison(ComparisonProblem.None, baseline, current, scoreChange, Judge(scoreChange, higherIsBetter), metrics, sameMachine);
    }

    public static ComparisonVerdict Judge(double changePercent, bool higherIsBetter)
    {
        if (Math.Abs(changePercent) < NoisePercent)
        {
            return ComparisonVerdict.WithinNoise;
        }

        return (changePercent > 0) == higherIsBetter ? ComparisonVerdict.Better : ComparisonVerdict.Worse;
    }

    private static BenchmarkComparison Unusable(ComparisonProblem problem, BenchmarkRecord baseline, BenchmarkRecord current, bool sameMachine) =>
        new(problem, baseline, current, 0, ComparisonVerdict.WithinNoise, [], sameMachine);
}
