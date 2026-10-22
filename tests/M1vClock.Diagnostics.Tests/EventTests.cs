using M1vClock.Core.Results;
using M1vClock.Diagnostics.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Diagnostics.Tests;

public sealed class FakeEventSource : IEventLogSource
{
    private readonly Dictionary<string, ReadResult<RawEventBatch>> _logs = new(StringComparer.Ordinal);

    public int Reads { get; private set; }

    public FakeEventSource With(string log, params RawEvent[] events) => With(log, false, events);

    public FakeEventSource With(string log, bool truncated, params RawEvent[] events)
    {
        _logs[log] = ReadResult.Success(new RawEventBatch(events, truncated));
        return this;
    }

    public FakeEventSource Failing(string log, string error)
    {
        _logs[log] = ReadResult.Failure<RawEventBatch>(error);
        return this;
    }

    public ReadResult<RawEventBatch> Read(string log, TimeSpan window, int limit, CancellationToken cancellationToken)
    {
        Reads++;
        return _logs.TryGetValue(log, out var result) ? result : ReadResult.Success(new RawEventBatch([], false));
    }
}

public sealed class EventGroupingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.FromHours(-3));

    private static IEnumerable<RawEvent> Repeat(string log, string provider, int id, EventSeverity severity, int count, string? message = null) =>
        Enumerable.Range(0, count).Select(i => new RawEvent(log, provider, id, severity, Now.AddMinutes(-i), i == 0 ? message : null));

    private static IReadOnlyList<RawEvent> ThisMachineFourteenDays() =>
    [
        .. Repeat("Application", "ESENT", 490, EventSeverity.Error, 15406),
        .. Repeat("Application", "ESENT", 455, EventSeverity.Error, 15406),
        .. Repeat("System", "Microsoft-Windows-DistributedCOM", 10016, EventSeverity.Error, 90),
        .. Repeat("System", "winsrvext", 100, EventSeverity.Warning, 61),
        .. Repeat("Application", "CoworkVMService", 1, EventSeverity.Error, 38),
        .. Repeat("System", "Microsoft-Windows-Time-Service", 135, EventSeverity.Warning, 36),
        .. Repeat("System", "Microsoft-Windows-Kernel-PnP", 219, EventSeverity.Warning, 19),
        .. Repeat("System", "TPM", 14, EventSeverity.Warning, 12),
        .. Repeat("System", "Microsoft-Windows-Kernel-Power", 41, EventSeverity.Critical, 4),
        .. Repeat("System", "EventLog", 6008, EventSeverity.Error, 4),
        .. Repeat("System", "Service Control Manager", 7000, EventSeverity.Error, 3),
    ];

    [Fact]
    public void EachProviderAndIdBecomesOneGroupWithItsCount()
    {
        var groups = EventService.Group(ThisMachineFourteenDays());

        Assert.Equal(11, groups.Count);
        Assert.Equal(15406, groups.Single(g => g.Provider == "ESENT" && g.Id == 490).Count);
        Assert.Equal(90, groups.Single(g => g.Id == 10016).Count);
        Assert.Equal(4, groups.Single(g => g.Provider == "Microsoft-Windows-Kernel-Power").Count);
    }

    [Fact]
    public void TheThirtyThousandNoisyEventsGoToTheBottomAndTheCriticalOnesComeFirst()
    {
        var groups = EventService.Group(ThisMachineFourteenDays());

        Assert.Equal("Microsoft-Windows-Kernel-Power", groups[0].Provider);
        Assert.Equal(EventSeverity.Critical, groups[0].Severity);
        Assert.All(groups.TakeLast(2), g => Assert.True(g.IsNoisy));
        Assert.Equal("ESENT", groups[^1].Provider);
        var quiet = groups.Where(g => g.Known is not { Kind: KnownEventKind.Noise } && !g.IsNoisy).Select(g => g.Severity).ToList();
        Assert.Equal(quiet.Order(), quiet);
    }

    [Fact]
    public void KnownNoiseSinksBelowTheRealProblemsEvenWhenItIsRare()
    {
        var groups = EventService.Group(
        [
            .. Repeat("System", "Microsoft-Windows-DistributedCOM", 10016, EventSeverity.Error, 90),
            .. Repeat("System", "disk", 153, EventSeverity.Warning, 2),
        ]);

        Assert.Equal("disk", groups[0].Provider);
        Assert.Equal(KnownEventKind.Noise, groups[1].Known?.Kind);
    }

    [Fact]
    public void ProvidersThatDifferOnlyByCaseShareAGroup()
    {
        var groups = EventService.Group(
        [
            new RawEvent("System", "Disk", 7, EventSeverity.Error, Now, null),
            new RawEvent("System", "disk", 7, EventSeverity.Warning, Now.AddMinutes(-1), null),
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Equal(EventSeverity.Error, group.Severity);
        Assert.Equal("Disk", group.Provider);
    }

    [Fact]
    public void TheSameEventNumberInTwoLogsStaysSeparate()
    {
        var groups = EventService.Group(
        [
            new RawEvent("System", "Service Control Manager", 7000, EventSeverity.Error, Now, null),
            new RawEvent("Application", "Service Control Manager", 7000, EventSeverity.Error, Now, null),
        ]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void ARangeOfTimesAndTheNewestMessageSurviveGrouping()
    {
        var groups = EventService.Group(
        [
            new RawEvent("System", "disk", 153, EventSeverity.Warning, Now.AddDays(-3), "velha"),
            new RawEvent("System", "disk", 153, EventSeverity.Warning, Now, "nova"),
            new RawEvent("System", "disk", 153, EventSeverity.Warning, Now.AddDays(-1), null),
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(Now.AddDays(-3), group.First);
        Assert.Equal(Now, group.Last);
        Assert.Equal("nova", group.Message);
    }

    [Fact]
    public void AMessageFallsBackToAnOlderOneWhenTheNewestHasNone()
    {
        var groups = EventService.Group(
        [
            new RawEvent("System", "disk", 153, EventSeverity.Warning, Now.AddDays(-1), "antiga"),
            new RawEvent("System", "disk", 153, EventSeverity.Warning, Now, null),
        ]);

        Assert.Equal("antiga", Assert.Single(groups).Message);
    }

    [Fact]
    public void LongMultiLineMessagesBecomeOneShortLine()
    {
        var text = "primeira linha\r\nsegunda linha\n" + new string('x', 900);
        var groups = EventService.Group([new RawEvent("System", "disk", 7, EventSeverity.Error, Now, text)]);

        var message = Assert.IsType<string>(Assert.Single(groups).Message);
        Assert.StartsWith("primeira linha segunda linha ", message, StringComparison.Ordinal);
        Assert.Equal(EventService.MessageLength, message.Length);
        Assert.EndsWith("…", message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', message);
    }

    [Fact]
    public void ABlankMessageIsNoMessage()
    {
        var groups = EventService.Group([new RawEvent("System", "disk", 7, EventSeverity.Error, Now, "  \r\n ")]);

        Assert.Null(Assert.Single(groups).Message);
    }

    [Fact]
    public void NothingToGroupGivesNoGroups()
    {
        Assert.Empty(EventService.Group([]));
    }

    [Fact]
    public void AGroupBecomesNoisyAtOneThousand()
    {
        var below = EventService.Group(Repeat("System", "x", 1, EventSeverity.Error, EventGroup.NoisyThreshold - 1));
        var at = EventService.Group(Repeat("System", "x", 1, EventSeverity.Error, EventGroup.NoisyThreshold));

        Assert.False(Assert.Single(below).IsNoisy);
        Assert.True(Assert.Single(at).IsNoisy);
    }
}

public sealed class EventServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.FromHours(-3));

    private static EventService Service(FakeEventSource source) => new(source, NullLogger<EventService>.Instance);

    private static RawEvent Raw(string log, string provider, int id, EventSeverity severity = EventSeverity.Error) => new(log, provider, id, severity, Now, null);

    [Fact]
    public async Task BothLogsAreReadAndMergedIntoOneReport()
    {
        var source = new FakeEventSource()
            .With("System", Raw("System", "disk", 7), Raw("System", "disk", 7))
            .With("Application", Raw("Application", ".NET Runtime", 1000));

        var report = await Service(source).ReadAsync(TimeSpan.FromDays(14), CancellationToken.None);

        Assert.Equal(2, source.Reads);
        Assert.Equal(2, report.Groups.Count);
        Assert.Equal(3, report.Total);
        Assert.Empty(report.FailedLogs);
        Assert.False(report.Truncated);
    }

    [Fact]
    public async Task ALogThatCannotBeReadIsReportedWhileTheOtherStillShows()
    {
        var source = new FakeEventSource()
            .Failing("System", "Sem permissão para ler o log System.")
            .With("Application", Raw("Application", ".NET Runtime", 1000));

        var report = await Service(source).ReadAsync(TimeSpan.FromDays(7), CancellationToken.None);

        Assert.Equal(".NET Runtime", Assert.Single(report.Groups).Provider);
        Assert.Equal("Sem permissão para ler o log System.", report.FailedLogs["System"]);
    }

    [Fact]
    public async Task BothLogsFailingGivesAnEmptyReportThatSaysWhy()
    {
        var source = new FakeEventSource().Failing("System", "a").Failing("Application", "b");

        var report = await Service(source).ReadAsync(TimeSpan.FromDays(1), CancellationToken.None);

        Assert.Empty(report.Groups);
        Assert.Equal(2, report.FailedLogs.Count);
    }

    [Fact]
    public async Task ATruncatedLogMarksTheWholeReport()
    {
        var source = new FakeEventSource().With("System", true, Raw("System", "disk", 7));

        var report = await Service(source).ReadAsync(TimeSpan.FromDays(30), CancellationToken.None);

        Assert.True(report.Truncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(91)]
    public async Task AWindowOutsideZeroToNinetyDaysIsRefused(int days)
    {
        var window = days == 0 ? TimeSpan.Zero : TimeSpan.FromDays(days);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service(new FakeEventSource()).ReadAsync(window, CancellationToken.None));
    }

    [Fact]
    public void TheReportCountsEventsBySeverity()
    {
        var groups = EventService.Group(
        [
            Raw("System", "a", 1, EventSeverity.Critical),
            Raw("System", "b", 1),
            Raw("System", "b", 1),
            Raw("System", "c", 1, EventSeverity.Warning),
        ]);
        var report = new EventReport(TimeSpan.FromDays(1), groups, new Dictionary<string, string>(), false);

        Assert.Equal(1, report.CountOf(EventSeverity.Critical));
        Assert.Equal(2, report.CountOf(EventSeverity.Error));
        Assert.Equal(1, report.CountOf(EventSeverity.Warning));
        Assert.Equal(4, report.Total);
    }
}

public sealed class EventKnowledgeTests
{
    [Theory]
    [InlineData("Microsoft-Windows-Kernel-Power", 41, "KernelPower41", KnownEventKind.UnexpectedShutdown)]
    [InlineData("EventLog", 6008, "UnexpectedShutdown6008", KnownEventKind.UnexpectedShutdown)]
    [InlineData("Microsoft-Windows-DistributedCOM", 10016, "DcomPermission", KnownEventKind.Noise)]
    [InlineData("ESENT", 490, "EsentFile", KnownEventKind.Noise)]
    [InlineData("Microsoft-Windows-Time-Service", 135, "TimeSync", KnownEventKind.Service)]
    [InlineData("Microsoft-Windows-Kernel-PnP", 219, "DriverNotLoaded", KnownEventKind.Hardware)]
    [InlineData("TPM", 14, "TpmProvisioning", KnownEventKind.Hardware)]
    [InlineData("disk", 153, "DiskRetried", KnownEventKind.Disk)]
    public void TheEventsSeenOnThisMachineAreExplained(string provider, int id, string key, KnownEventKind kind)
    {
        var known = EventKnowledge.Find(provider, id);

        Assert.NotNull(known);
        Assert.Equal(key, known.Key);
        Assert.Equal(kind, known.Kind);
    }

    [Fact]
    public void TheProviderNameIsMatchedWithoutRegardToCase()
    {
        Assert.Equal("DiskRetried", EventKnowledge.Find("DISK", 153)?.Key);
        Assert.Equal("KernelPower41", EventKnowledge.Find("microsoft-windows-kernel-power", 41)?.Key);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void EveryWheaEventIsAHardwareError(int id)
    {
        Assert.Equal(KnownEventKind.Hardware, EventKnowledge.Find("Microsoft-Windows-WHEA-Logger", id)?.Kind);
    }

    [Fact]
    public void AnUnknownEventHasNoExplanationAndTheSameIdOnAnotherProviderIsNotConfused()
    {
        Assert.Null(EventKnowledge.Find("winsrvext", 100));
        Assert.Null(EventKnowledge.Find("CoworkVMService", 1));
        Assert.Null(EventKnowledge.Find("OutroProvedor", 41));
        Assert.Null(EventKnowledge.Find("Microsoft-Windows-Kernel-Power", 42));
    }
}
