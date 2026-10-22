namespace M1vClock.Benchmarks.Statistics;

public static class BenchStatistics
{
    public static double Mean(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return 0;
        }

        var sum = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
        }

        return sum / values.Count;
    }

    public static double Median(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = Sorted(values);
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    public static double Percentile(IReadOnlyList<double> values, double fraction)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfLessThan(fraction, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fraction, 1);
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = Sorted(values);
        return sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * fraction))];
    }

    public static double LowFps(IReadOnlyList<double> frameMilliseconds, double slowestFraction = 0.01)
    {
        ArgumentNullException.ThrowIfNull(frameMilliseconds);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(slowestFraction, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slowestFraction, 1);
        var threshold = Percentile(frameMilliseconds, 1 - slowestFraction);
        return threshold > 0 ? 1000 / threshold : 0;
    }

    public static double SpreadPercent(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            return 0;
        }

        var median = Median(values);
        if (median <= 0)
        {
            return 0;
        }

        var sorted = Sorted(values);
        return (sorted[^1] - sorted[0]) / median * 100;
    }

    public static double DifferencePercent(double first, double second)
    {
        var average = (Math.Abs(first) + Math.Abs(second)) / 2;
        return average <= 0 ? 0 : Math.Abs(first - second) / average * 100;
    }

    public static double ChangePercent(double baseline, double current)
    {
        return baseline == 0 ? 0 : (current - baseline) / Math.Abs(baseline) * 100;
    }

    private static double[] Sorted(IReadOnlyList<double> values)
    {
        var copy = new double[values.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = values[i];
        }

        Array.Sort(copy);
        return copy;
    }
}
