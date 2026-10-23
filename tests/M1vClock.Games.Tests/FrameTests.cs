using M1vClock.Core.Security;
using M1vClock.Games.Frames;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Games.Tests;

public sealed class FrameWindowTests
{
    private static FrameWindow Steady(double frameMs, int frames, double start = 0)
    {
        var window = new FrameWindow();
        for (var i = 0; i < frames; i++)
        {
            window.Add(start + i * frameMs);
        }

        return window;
    }

    [Fact]
    public void FewerThanThreePresentsGiveNoStatistics()
    {
        Assert.Null(new FrameWindow().Compute(1, "x"));
        Assert.Null(Steady(16, 2).Compute(1, "x"));
        Assert.NotNull(Steady(16, 3).Compute(1, "x"));
    }

    [Fact]
    public void ASteadySixtyFramesPerSecondGivesSixtyFpsAndTheSameOnePercentLow()
    {
        var stats = Steady(1000.0 / 60, 120).Compute(7, "jogo")!;

        Assert.Equal(7, stats.ProcessId);
        Assert.Equal("jogo", stats.ProcessName);
        Assert.Equal(60, stats.Fps, 1);
        Assert.Equal(16.67, stats.FrameTimeMs, 1);
        Assert.Equal(60, stats.OnePercentLowFps, 1);
    }

    [Fact]
    public void OneLongFrameInAHundredDragsTheOnePercentLowButBarelyMovesTheAverage()
    {
        var window = new FrameWindow();
        var time = 0.0;
        window.Add(time);
        for (var i = 0; i < 99; i++)
        {
            time += 10;
            window.Add(time);
        }

        time += 100;
        window.Add(time);

        var stats = window.Compute(1, "x")!;

        Assert.Equal(10, stats.OnePercentLowFps, 1);
        Assert.True(stats.Fps > 40, $"fps {stats.Fps}");
    }

    [Fact]
    public void TheFpsComesFromTheLastSecondOnly()
    {
        var window = Steady(1000.0 / 30, 150);
        var start = 150 * 1000.0 / 30;
        for (var i = 0; i < 120; i++)
        {
            window.Add(start + i * 1000.0 / 120);
        }

        var stats = window.Compute(1, "x")!;

        Assert.Equal(120, stats.Fps, 0);
    }

    [Fact]
    public void OnlyTheLastTenSecondsAreKept()
    {
        var window = Steady(10, 3000);

        Assert.InRange(window.Count, 990, 1002);
    }

    [Fact]
    public void LateEventsAreInsertedInOrderAndExactRepeatsAreIgnored()
    {
        var window = new FrameWindow();
        window.Add(10);
        window.Add(20);
        window.Add(20);
        window.Add(15);
        window.Add(30);

        Assert.Equal(4, window.Count);
    }

    [Fact]
    public void EventsDeliveredOutOfOrderByDifferentProcessorBuffersGiveTheSameResultAsInOrder()
    {
        var ordered = Steady(1000.0 / 187, 600).Compute(1, "x")!;
        var shuffled = new FrameWindow();
        var stamps = Enumerable.Range(0, 600).Select(i => i * 1000.0 / 187).ToList();
        foreach (var t in stamps.Where((_, i) => i % 3 == 0).Concat(stamps.Where((_, i) => i % 3 == 1)).Concat(stamps.Where((_, i) => i % 3 == 2)))
        {
            shuffled.Add(t);
        }

        var stats = shuffled.Compute(1, "x")!;

        Assert.Equal(600, shuffled.Count);
        Assert.Equal(ordered.Fps, stats.Fps, 3);
        Assert.Equal(ordered.OnePercentLowFps, stats.OnePercentLowFps, 3);
        Assert.Equal(187, stats.Fps, 0);
    }

    [Fact]
    public void ALateEventOlderThanTheWindowIsDroppedRightAway()
    {
        var window = Steady(10, 3000);
        var before = window.Count;

        window.Add(5);

        Assert.Equal(before, window.Count);
    }

    [Fact]
    public void OnlyTheLastHundredAndTwentyFrameTimesAreReturnedForTheGraph()
    {
        var stats = Steady(10, 500).Compute(1, "x")!;

        Assert.Equal(FrameWindow.RecentCount, stats.RecentFrameTimes.Count);
        Assert.All(stats.RecentFrameTimes, t => Assert.Equal(10, t, 6));
    }
}

public sealed class FrameMonitorTests
{
    private sealed class FakeSource : IFrameEventSource
    {
        public FrameSourceResult Result { get; set; } = FrameSourceResult.Started;

        public int Starts { get; private set; }

        public int Shutdowns { get; private set; }

        public bool Disposed { get; private set; }

        public bool HasListeners => Presented is not null;

        public event Action<FramePresent>? Presented;

        public FrameSourceResult Start()
        {
            Starts++;
            return Result;
        }

        public void Shutdown() => Shutdowns++;

        public void Dispose() => Disposed = true;

        public void Present(int processId, double timestampMs) => Presented?.Invoke(new FramePresent(processId, timestampMs));
    }

    private sealed class Names : IProcessNames
    {
        public string NameOf(int processId) => $"jogo{processId}";
    }

    private sealed class Foreground(int? processId) : IForegroundWindow
    {
        public int? Id { get; set; } = processId;

        public int? ProcessId() => Id;
    }

    private static (FrameMonitor Monitor, FakeSource Source, FakeTimeProvider Clock, Foreground Foreground) Create(int? foreground = null)
    {
        var source = new FakeSource();
        var clock = new FakeTimeProvider();
        var front = new Foreground(foreground);
        return (new FrameMonitor(source, new Names(), front, clock, NullLogger<FrameMonitor>.Instance), source, clock, front);
    }

    private static void Frames(FakeSource source, int processId, int count, double start = 0, double step = 10)
    {
        for (var i = 0; i < count; i++)
        {
            source.Present(processId, start + i * step);
        }
    }

    [Fact]
    public void TheSessionStartsWithTheFirstLeaseAndStopsWithTheLastOne()
    {
        var (monitor, source, _, _) = Create();

        var first = monitor.Acquire();
        var second = monitor.Acquire();

        Assert.Equal(1, source.Starts);
        Assert.Equal(FrameMonitorState.Running, monitor.State);

        first.Dispose();
        first.Dispose();
        Assert.Equal(0, source.Shutdowns);

        second.Dispose();
        Assert.Equal(1, source.Shutdowns);
        Assert.Equal(FrameMonitorState.Stopped, monitor.State);
        Assert.False(source.HasListeners);
    }

    [Fact]
    public void AcquiringAgainAfterReleasingStartsANewSession()
    {
        var (monitor, source, _, _) = Create();
        monitor.Acquire().Dispose();

        using var again = monitor.Acquire();

        Assert.Equal(2, source.Starts);
        Assert.True(source.HasListeners);
    }

    [Fact]
    public void PresentsBecomeStatisticsWithTheProcessName()
    {
        var (monitor, source, _, _) = Create();
        using var lease = monitor.Acquire();

        Frames(source, 42, 120, step: 1000.0 / 60);

        var stats = monitor.GetStats(42)!;
        Assert.Equal("jogo42", stats.ProcessName);
        Assert.Equal(60, stats.Fps, 1);
        Assert.Null(monitor.GetStats(43));
    }

    [Fact]
    public void AProcessThatStoppedPresentingForMoreThanTwoSecondsGivesNothing()
    {
        var (monitor, source, clock, _) = Create();
        using var lease = monitor.Acquire();
        Frames(source, 42, 30);

        clock.Advance(TimeSpan.FromSeconds(1.9));
        Assert.NotNull(monitor.GetStats(42));

        clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Null(monitor.GetStats(42));
    }

    [Fact]
    public void ProcessesNotSeenForThirtySecondsAreForgottenOnTheNextPresent()
    {
        var (monitor, source, clock, _) = Create();
        using var lease = monitor.Acquire();
        Frames(source, 1, 30);
        Frames(source, 2, 30);

        clock.Advance(TimeSpan.FromSeconds(31));
        Frames(source, 3, 30);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        source.Present(3, 300);

        Assert.Null(monitor.GetStats(1));
        Assert.Null(monitor.GetStats(2));
        Assert.NotNull(monitor.GetStats(3));
    }

    [Fact]
    public void WithoutAdministratorTheStateSaysSoAndThereAreNoStatistics()
    {
        var (monitor, source, _, _) = Create();
        source.Result = FrameSourceResult.NeedsAdministrator;

        using var lease = monitor.Acquire();

        Assert.Equal(FrameMonitorState.NeedsAdministrator, monitor.State);
        Assert.Null(monitor.GetStats(1));
    }

    [Fact]
    public void AFailedSessionIsReportedAsFailed()
    {
        var (monitor, source, _, _) = Create();
        source.Result = FrameSourceResult.Failed;

        using var lease = monitor.Acquire();

        Assert.Equal(FrameMonitorState.Failed, monitor.State);
        lease.Dispose();
        Assert.Equal(0, source.Shutdowns);
    }

    [Fact]
    public void TheForegroundStatsAreTheOnesOfTheProgramOnScreenAndNeverOurOwnWindow()
    {
        var (monitor, source, _, front) = Create(foreground: 42);
        using var lease = monitor.Acquire();
        Frames(source, 42, 30);
        Frames(source, Environment.ProcessId, 30);
        Frames(source, 77, 30);

        Assert.Equal("jogo42", monitor.GetForegroundStats()!.ProcessName);

        front.Id = Environment.ProcessId;
        Assert.Null(monitor.GetForegroundStats());

        front.Id = null;
        Assert.Null(monitor.GetForegroundStats());

        front.Id = 0;
        Assert.Null(monitor.GetForegroundStats());

        front.Id = 999;
        Assert.Null(monitor.GetForegroundStats());
    }

    [Fact]
    public void PresentsThatArriveAfterTheLastLeaseWasReleasedAreIgnored()
    {
        var (monitor, source, _, _) = Create();
        monitor.Acquire().Dispose();

        Frames(source, 42, 30);

        Assert.Null(monitor.GetStats(42));
    }

    [Fact]
    public void DisposingTheMonitorStopsTheSessionAndDisposesTheSource()
    {
        var (monitor, source, _, _) = Create();
        _ = monitor.Acquire();

        monitor.Dispose();

        Assert.Equal(1, source.Shutdowns);
        Assert.True(source.Disposed);
        Assert.Equal(FrameMonitorState.Stopped, monitor.State);
    }
}

public sealed class FrameSourcesTests
{
    [Fact]
    public void WithoutAdministratorTheEtwSourceNeverTouchesEtwAndAsksForIt()
    {
        using var source = new EtwFrameEventSource(new ElevationInfo(false, "S-1-5-21-1", @"PC\joao"), NullLogger<EtwFrameEventSource>.Instance);

        Assert.Equal(FrameSourceResult.NeedsAdministrator, source.Start());
        source.Shutdown();
    }

    [Fact]
    public void ThisPcNamesTheCurrentProcessAndFallsBackForAProcessThatDoesNotExist()
    {
        var names = new WindowsProcessNames(NullLogger<WindowsProcessNames>.Instance);

        Assert.False(string.IsNullOrWhiteSpace(names.NameOf(Environment.ProcessId)));
        Assert.DoesNotContain("PID", names.NameOf(Environment.ProcessId), StringComparison.Ordinal);
        Assert.Equal("PID 2147483000", names.NameOf(2_147_483_000));
    }

    [Fact]
    public void TheForegroundWindowOfThisPcIsReadWithoutFailing()
    {
        var id = new WindowsForegroundWindow().ProcessId();

        Assert.True(id is null or > 0);
    }
}
