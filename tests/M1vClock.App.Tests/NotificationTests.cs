using M1vClock.App.Services;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.App.Tests;

internal sealed class InlineUiThread : IUiThread
{
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}

public sealed class NotificationServiceTests
{
    private static ToastRequest Request(string title) => new(title, "Texto", "Icon.Devices", ToastTone.Info);

    private static (NotificationService Service, FakeTimeProvider Clock) Create()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
        return (new NotificationService(new InlineUiThread(), clock), clock);
    }

    [Fact]
    public void ShowingAddsAToastWithItsContent()
    {
        var (service, _) = Create();

        service.Show(new ToastRequest("Pendrive conectado", "Kingston (E:)", "Icon.Storage", ToastTone.Success));

        var toast = Assert.Single(service.Toasts);
        Assert.Equal("Pendrive conectado", toast.Title);
        Assert.Equal("Kingston (E:)", toast.Text);
        Assert.Equal("Icon.Storage", toast.IconKey);
        Assert.Equal(ToastTone.Success, toast.Tone);
        Assert.False(toast.IsExiting);
    }

    [Fact]
    public void AToastLeavesOnItsOwnAfterTheLifetimeAndTheExitAnimation()
    {
        var (service, clock) = Create();
        service.Show(Request("A"));

        clock.Advance(NotificationService.Lifetime - TimeSpan.FromMilliseconds(1));
        Assert.False(service.Toasts[0].IsExiting);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(service.Toasts[0].IsExiting);
        Assert.Single(service.Toasts);
        clock.Advance(NotificationService.ExitDuration);

        Assert.Empty(service.Toasts);
    }

    [Fact]
    public void DismissingByHandStartsTheExitRightAway()
    {
        var (service, clock) = Create();
        service.Show(Request("A"));

        service.Toasts[0].DismissCommand.Execute(null);

        Assert.True(service.Toasts[0].IsExiting);
        clock.Advance(NotificationService.ExitDuration);
        Assert.Empty(service.Toasts);
    }

    [Fact]
    public void DismissingTwiceDoesNotScheduleASecondExit()
    {
        var (service, clock) = Create();
        service.Show(Request("A"));
        var toast = service.Toasts[0];

        toast.DismissCommand.Execute(null);
        clock.Advance(NotificationService.ExitDuration / 2);
        toast.DismissCommand.Execute(null);
        clock.Advance(NotificationService.ExitDuration / 2);

        Assert.Empty(service.Toasts);
    }

    [Fact]
    public void OnlyThreeToastsStayVisibleAndTheOldestLeaves()
    {
        var (service, clock) = Create();
        foreach (var title in new[] { "1", "2", "3", "4" })
        {
            service.Show(Request(title));
        }

        Assert.True(service.Toasts[0].IsExiting);
        Assert.Equal(["2", "3", "4"], service.Toasts.Where(t => !t.IsExiting).Select(t => t.Title));
        clock.Advance(NotificationService.ExitDuration);
        Assert.Equal(["2", "3", "4"], service.Toasts.Select(t => t.Title));
    }

    [Fact]
    public void ALaterToastKeepsItsOwnClockAfterAnEarlierOneIsGone()
    {
        var (service, clock) = Create();
        service.Show(Request("A"));
        service.Toasts[0].DismissCommand.Execute(null);
        clock.Advance(NotificationService.ExitDuration);

        service.Show(Request("B"));
        clock.Advance(NotificationService.Lifetime);
        Assert.True(service.Toasts[0].IsExiting);
        clock.Advance(NotificationService.ExitDuration);

        Assert.Empty(service.Toasts);
    }

    [Fact]
    public void ARequestIsRequired()
    {
        var (service, _) = Create();

        Assert.Throws<ArgumentNullException>(() => service.Show(null!));
    }
}

public sealed class ActivityLogTests
{
    private static ActivityEntry Entry(string title, int minute = 0) =>
        new(new DateTimeOffset(2026, 10, 21, 12, minute, 0, TimeSpan.Zero), "Icon.Devices", title, "Texto");

    [Fact]
    public void NewestEntriesComeFirst()
    {
        var log = new ActivityLog(new InlineUiThread());

        log.Add(Entry("antigo", 1));
        log.Add(Entry("novo", 2));

        Assert.Equal(["novo", "antigo"], log.Entries.Select(e => e.Title));
    }

    [Fact]
    public void OnlyTheLatestFiftyAreKept()
    {
        var log = new ActivityLog(new InlineUiThread());

        for (var i = 0; i < ActivityLog.Capacity + 10; i++)
        {
            log.Add(Entry($"#{i}"));
        }

        Assert.Equal(ActivityLog.Capacity, log.Entries.Count);
        Assert.Equal($"#{ActivityLog.Capacity + 9}", log.Entries[0].Title);
        Assert.Equal("#10", log.Entries[^1].Title);
    }

    [Fact]
    public void AddingRaisesChangedOnTheUiThread()
    {
        var posted = new List<Action>();
        var log = new ActivityLog(new QueueUiThread(posted));
        var raised = 0;
        log.Changed += (_, _) => raised++;

        log.Add(Entry("a"));
        Assert.Equal(0, raised);
        Assert.Single(posted);
        posted[0]();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void TheEntriesListIsASnapshot()
    {
        var log = new ActivityLog(new InlineUiThread());
        log.Add(Entry("a"));
        var before = log.Entries;

        log.Add(Entry("b"));

        Assert.Single(before);
        Assert.Equal(2, log.Entries.Count);
    }

    private sealed class QueueUiThread(List<Action> posted) : IUiThread
    {
        public void Post(Action action) => posted.Add(action);
    }
}
