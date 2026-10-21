using M1vClock.Core.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Core.Tests;

public sealed class SessionTrackerTests
{
    [Fact]
    public void FirstStartHasNoPreviousSession()
    {
        using var temp = new TempDirectory();
        var tracker = new SessionTracker(temp.Path, new FakeTimeProvider());

        Assert.Null(tracker.Start("0.1.0"));
    }

    [Fact]
    public void CleanExitIsNotReportedAsCrash()
    {
        using var temp = new TempDirectory();
        var tracker = new SessionTracker(temp.Path, new FakeTimeProvider());
        tracker.Start("0.1.0");
        tracker.EndCleanly();

        Assert.Null(new SessionTracker(temp.Path, new FakeTimeProvider()).Start("0.1.0"));
    }

    [Fact]
    public void SessionThatNeverEndedIsReportedWithCrashDetails()
    {
        using var temp = new TempDirectory();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero));
        var tracker = new SessionTracker(temp.Path, time);
        tracker.Start("0.1.0");
        tracker.RecordCrash(new InvalidOperationException("Falha simulada"));

        var previous = new SessionTracker(temp.Path, time).Start("0.1.1");

        Assert.NotNull(previous);
        Assert.Equal("0.1.0", previous.Version);
        Assert.NotNull(previous.Crash);
        Assert.Equal("Falha simulada", previous.Crash.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, previous.Crash.ExceptionType);
    }

    [Fact]
    public void CrashIsReportedOnlyOnce()
    {
        using var temp = new TempDirectory();
        var tracker = new SessionTracker(temp.Path, new FakeTimeProvider());
        tracker.Start("0.1.0");
        tracker.RecordCrash(new InvalidOperationException("x"));
        var second = new SessionTracker(temp.Path, new FakeTimeProvider());
        second.Start("0.1.0");
        second.EndCleanly();

        Assert.Null(new SessionTracker(temp.Path, new FakeTimeProvider()).Start("0.1.0"));
    }
}
