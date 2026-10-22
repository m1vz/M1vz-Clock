using M1vClock.Core.Results;
using M1vClock.Diagnostics.Reliability;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Diagnostics.Tests;

public sealed class ReliabilityClassifierTests
{
    [Theory]
    [InlineData("MsiInstaller", 1033, ReliabilityKind.SoftwareInstalled)]
    [InlineData("MsiInstaller", 1034, ReliabilityKind.SoftwareRemoved)]
    [InlineData("MsiInstaller", 1035, ReliabilityKind.SoftwareReconfigured)]
    [InlineData("MsiInstaller", 1038, ReliabilityKind.Other)]
    [InlineData("Microsoft-Windows-WindowsUpdateClient", 19, ReliabilityKind.UpdateInstalled)]
    [InlineData("Microsoft-Windows-WindowsUpdateClient", 20, ReliabilityKind.UpdateFailed)]
    [InlineData("EventLog", 6008, ReliabilityKind.UnexpectedShutdown)]
    [InlineData("Application Error", 1000, ReliabilityKind.AppCrash)]
    [InlineData("Application Hang", 1002, ReliabilityKind.AppCrash)]
    [InlineData("Microsoft-Windows-WER-SystemErrorReporting", 1001, ReliabilityKind.WindowsFailure)]
    [InlineData("OutraOrigem", 1000, ReliabilityKind.Other)]
    public void EverySourceAndIdSeenOnThisPcHasItsKind(string source, int id, ReliabilityKind kind)
    {
        Assert.Equal(kind, ReliabilityClassifier.Classify(source, id));
    }

    [Fact]
    public void TheSourceNameIsMatchedWithoutRegardToCase()
    {
        Assert.Equal(ReliabilityKind.SoftwareInstalled, ReliabilityClassifier.Classify("msiinstaller", 1033));
        Assert.Equal(ReliabilityKind.AppCrash, ReliabilityClassifier.Classify("APPLICATION ERROR", 1000));
    }

    [Fact]
    public void OnlyFailuresCountAsFailures()
    {
        var at = DateTimeOffset.UnixEpoch;
        var failures = Enum.GetValues<ReliabilityKind>().Where(k => new ReliabilityRecord(at, "x", 1, "p", k).IsFailure).ToList();

        Assert.Equal(
            [ReliabilityKind.UpdateFailed, ReliabilityKind.UnexpectedShutdown, ReliabilityKind.AppCrash, ReliabilityKind.WindowsFailure],
            failures);
    }
}

public sealed class ReliabilityReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    private static ReadResult<IReadOnlyList<StabilityPoint>> Points(params double[] values) =>
        ReadResult.Success<IReadOnlyList<StabilityPoint>>(values.Select((v, i) => new StabilityPoint(Now.AddHours(i - values.Length), v)).ToList());

    private static ReliabilityReport Report(ReadResult<IReadOnlyList<StabilityPoint>> stability, params ReliabilityKind[] kinds) =>
        new(TimeSpan.FromDays(30), stability, ReadResult.Success<IReadOnlyList<ReliabilityRecord>>(kinds.Select((k, i) => new ReliabilityRecord(Now.AddMinutes(-i), "x", 1, "p", k)).ToList()));

    [Fact]
    public void TheNumbersOfThisPcGiveTheCurrentLowestAndAverageIndex()
    {
        var report = Report(Points(5.842, 7.4, 7.05));

        Assert.Equal(7.05, report.Current);
        Assert.Equal(5.842, report.Lowest);
        Assert.Equal((5.842 + 7.4 + 7.05) / 3, report.Average);
        Assert.Equal(StabilityVerdict.Occasional, report.Verdict);
    }

    [Theory]
    [InlineData(10.0, StabilityVerdict.Stable)]
    [InlineData(8.0, StabilityVerdict.Stable)]
    [InlineData(7.99, StabilityVerdict.Occasional)]
    [InlineData(5.0, StabilityVerdict.Occasional)]
    [InlineData(4.99, StabilityVerdict.Unstable)]
    [InlineData(1.0, StabilityVerdict.Unstable)]
    public void TheVerdictFollowsTheEightAndFiveLines(double index, StabilityVerdict verdict)
    {
        Assert.Equal(verdict, ReliabilityReport.VerdictOf(index));
    }

    [Fact]
    public void NoReadingMeansNoNumbersAndAnUnknownVerdict()
    {
        var empty = Report(Points());
        var failed = Report(ReadResult.Failure<IReadOnlyList<StabilityPoint>>("sem WMI"));

        Assert.All(new[] { empty, failed }, r =>
        {
            Assert.Null(r.Current);
            Assert.Null(r.Lowest);
            Assert.Null(r.Average);
            Assert.Equal(StabilityVerdict.Unknown, r.Verdict);
        });
    }

    [Fact]
    public void RecordsAreCountedByKindAndFailuresTogether()
    {
        var report = Report(Points(8), ReliabilityKind.SoftwareInstalled, ReliabilityKind.SoftwareInstalled, ReliabilityKind.AppCrash, ReliabilityKind.UnexpectedShutdown, ReliabilityKind.UpdateInstalled);

        Assert.Equal(2, report.Count(ReliabilityKind.SoftwareInstalled));
        Assert.Equal(0, report.Count(ReliabilityKind.UpdateFailed));
        Assert.Equal(2, report.Failures);
    }

    [Fact]
    public void RecordsThatFailedToReadCountAsZero()
    {
        var report = new ReliabilityReport(TimeSpan.FromDays(7), Points(8), ReadResult.Failure<IReadOnlyList<ReliabilityRecord>>("x"));

        Assert.Equal(0, report.Failures);
        Assert.Equal(0, report.Count(ReliabilityKind.AppCrash));
    }
}

internal sealed class FakeReliabilitySource : IReliabilitySource
{
    public ReadResult<IReadOnlyList<StabilityPoint>> Stability { get; set; } = ReadResult.Success<IReadOnlyList<StabilityPoint>>([]);

    public ReadResult<IReadOnlyList<ReliabilityRecord>> Records { get; set; } = ReadResult.Success<IReadOnlyList<ReliabilityRecord>>([]);

    public TimeSpan? Asked { get; private set; }

    public ReadResult<IReadOnlyList<StabilityPoint>> ReadStability(TimeSpan window, CancellationToken cancellationToken)
    {
        Asked = window;
        return Stability;
    }

    public ReadResult<IReadOnlyList<ReliabilityRecord>> ReadRecords(TimeSpan window, CancellationToken cancellationToken) => Records;
}

public sealed class ReliabilityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PointsComeOldestFirstAndRecordsNewestFirst()
    {
        var source = new FakeReliabilitySource
        {
            Stability = ReadResult.Success<IReadOnlyList<StabilityPoint>>([new StabilityPoint(Now, 7), new StabilityPoint(Now.AddHours(-2), 6), new StabilityPoint(Now.AddHours(-1), 6.5)]),
            Records = ReadResult.Success<IReadOnlyList<ReliabilityRecord>>(
            [
                new ReliabilityRecord(Now.AddDays(-2), "MsiInstaller", 1033, "A", ReliabilityKind.SoftwareInstalled),
                new ReliabilityRecord(Now, "EventLog", 6008, "Windows", ReliabilityKind.UnexpectedShutdown),
            ]),
        };

        var report = await new ReliabilityService(source).ReadAsync(TimeSpan.FromDays(14), CancellationToken.None);

        Assert.Equal(TimeSpan.FromDays(14), source.Asked);
        Assert.True(report.Stability.TryGet(out var points));
        Assert.Equal([6.0, 6.5, 7.0], points.Select(p => p.Index));
        Assert.True(report.Records.TryGet(out var records));
        Assert.Equal(["EventLog", "MsiInstaller"], records.Select(r => r.Source));
        Assert.Equal(7, report.Current);
    }

    [Fact]
    public async Task AFailedPartDoesNotHideTheOther()
    {
        var source = new FakeReliabilitySource
        {
            Stability = ReadResult.Failure<IReadOnlyList<StabilityPoint>>("WMI quebrado"),
            Records = ReadResult.Success<IReadOnlyList<ReliabilityRecord>>([new ReliabilityRecord(Now, "EventLog", 6008, "Windows", ReliabilityKind.UnexpectedShutdown)]),
        };

        var report = await new ReliabilityService(source).ReadAsync(TimeSpan.FromDays(30), CancellationToken.None);

        Assert.False(report.Stability.IsSuccess);
        Assert.Equal("WMI quebrado", report.Stability.Error);
        Assert.Equal(1, report.Failures);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task AWindowOutsideZeroToNinetyDaysIsRefused(int days)
    {
        var window = days == 0 ? TimeSpan.Zero : TimeSpan.FromDays(days);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new ReliabilityService(new FakeReliabilitySource()).ReadAsync(window, CancellationToken.None));
    }
}

public sealed class RealMachineReliabilityTests
{
    private static readonly WmiReliabilitySource Source = new(NullLogger<WmiReliabilitySource>.Instance);

    [Fact]
    public void TheStabilityIndexOfThisPcIsReadFromWmiAndStaysBetweenOneAndTen()
    {
        var result = Source.ReadStability(TimeSpan.FromDays(30), CancellationToken.None);

        Assert.True(result.TryGet(out var points), result.Error);
        var cutoff = DateTimeOffset.Now.AddDays(-30).AddHours(-2);
        Assert.All(points, p =>
        {
            Assert.InRange(p.Index, 1.0, 10.0);
            Assert.True(p.At >= cutoff, $"{p.At} fora da janela");
        });
    }

    [Fact]
    public void TheRecordsOfThisPcAreReadAndEveryOneHasASourceAndATime()
    {
        var result = Source.ReadRecords(TimeSpan.FromDays(30), CancellationToken.None);

        Assert.True(result.TryGet(out var records), result.Error);
        var cutoff = DateTimeOffset.Now.AddDays(-30).AddHours(-2);
        Assert.All(records, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Source));
            Assert.True(r.Time >= cutoff);
            Assert.Equal(ReliabilityClassifier.Classify(r.Source, r.Id), r.Kind);
        });
    }

    [Fact]
    public async Task TheWholeServiceRunsAgainstThisPc()
    {
        var report = await new ReliabilityService(Source).ReadAsync(TimeSpan.FromDays(14), CancellationToken.None);

        Assert.True(report.Stability.IsSuccess, report.Stability.Error);
        Assert.True(report.Records.IsSuccess, report.Records.Error);
        if (report.Current is { } current)
        {
            Assert.InRange(current, 1.0, 10.0);
            Assert.NotEqual(StabilityVerdict.Unknown, report.Verdict);
        }
    }

    [Fact]
    public void ACancelledReadStopsInsteadOfFinishing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => Source.ReadRecords(TimeSpan.FromDays(30), cancelled.Token));
    }
}
