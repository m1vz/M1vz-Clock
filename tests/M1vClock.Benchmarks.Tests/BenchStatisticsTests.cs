using M1vClock.Benchmarks.Statistics;

namespace M1vClock.Benchmarks.Tests;

public sealed class BenchStatisticsTests
{
    [Fact]
    public void TheMedianOfAnOddListIsTheMiddleValueWhateverTheOrder()
    {
        Assert.Equal(5, BenchStatistics.Median([9, 1, 5]));
    }

    [Fact]
    public void TheMedianOfAnEvenListIsTheAverageOfTheTwoMiddleValues()
    {
        Assert.Equal(2.5, BenchStatistics.Median([4, 1, 3, 2]));
    }

    [Fact]
    public void EmptyListsGiveZeroInsteadOfFailing()
    {
        Assert.Equal(0, BenchStatistics.Median([]));
        Assert.Equal(0, BenchStatistics.Mean([]));
        Assert.Equal(0, BenchStatistics.Percentile([], 0.99));
        Assert.Equal(0, BenchStatistics.LowFps([]));
        Assert.Equal(0, BenchStatistics.SpreadPercent([]));
    }

    [Fact]
    public void TheListGivenIsNeverReordered()
    {
        List<double> values = [3, 1, 2];

        BenchStatistics.Median(values);
        BenchStatistics.Percentile(values, 0.5);

        Assert.Equal([3, 1, 2], values);
    }

    [Fact]
    public void OneSlowFrameInAHundredSetsTheLowFpsLikeTheOriginalDid()
    {
        var frames = Enumerable.Repeat(10.0, 99).Append(50.0).ToList();

        Assert.Equal(20, BenchStatistics.LowFps(frames), 6);
    }

    [Fact]
    public void ASteadyRunHasTheSameLowAndAverageFps()
    {
        var frames = Enumerable.Repeat(8.0, 500).ToList();

        Assert.Equal(125, BenchStatistics.LowFps(frames), 6);
    }

    [Fact]
    public void ThePercentileNeverReadsPastTheEnd()
    {
        Assert.Equal(7, BenchStatistics.Percentile([7], 1));
        Assert.Equal(3, BenchStatistics.Percentile([1, 2, 3], 1));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void AFractionOutsideZeroToOneIsRejected(double fraction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchStatistics.Percentile([1, 2], fraction));
    }

    [Fact]
    public void TheSpreadIsTheRangeOverTheMedian()
    {
        Assert.Equal(10, BenchStatistics.SpreadPercent([95, 100, 105]), 6);
    }

    [Fact]
    public void ASingleRunHasNoSpread()
    {
        Assert.Equal(0, BenchStatistics.SpreadPercent([100]));
    }

    [Fact]
    public void TheDifferenceBetweenTwoRunsDoesNotDependOnWhichCameFirst()
    {
        Assert.Equal(BenchStatistics.DifferencePercent(100, 103), BenchStatistics.DifferencePercent(103, 100), 9);
        Assert.Equal(300.0 / 101.5, BenchStatistics.DifferencePercent(100, 103), 9);
    }

    [Fact]
    public void TwoZeroResultsDoNotDivideByZero()
    {
        Assert.Equal(0, BenchStatistics.DifferencePercent(0, 0));
        Assert.Equal(0, BenchStatistics.ChangePercent(0, 5));
    }

    [Fact]
    public void TheChangeIsRelativeToTheBaseline()
    {
        Assert.Equal(25, BenchStatistics.ChangePercent(80, 100), 9);
        Assert.Equal(-50, BenchStatistics.ChangePercent(200, 100), 9);
    }
}
