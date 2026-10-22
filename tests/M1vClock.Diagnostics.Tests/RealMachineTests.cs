using M1vClock.Diagnostics.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Diagnostics.Tests;

public sealed class RealMachineEventTests
{
    private static readonly WindowsEventLogSource Source = new(NullLogger<WindowsEventLogSource>.Instance);

    [Fact]
    public void TheSystemLogOfThisPcReadsWithoutAdministrator()
    {
        var result = Source.Read("System", TimeSpan.FromDays(7), 5000, CancellationToken.None);

        Assert.True(result.TryGet(out var batch), result.Error);
        var cutoff = DateTimeOffset.Now.AddDays(-7).AddMinutes(-1);
        Assert.All(batch.Events, e =>
        {
            Assert.Equal("System", e.Log);
            Assert.False(string.IsNullOrWhiteSpace(e.Provider));
            Assert.InRange((int)e.Severity, 1, 3);
            Assert.True(e.Time >= cutoff, $"{e.Provider} {e.Id} em {e.Time}");
        });
    }

    [Fact]
    public void AMessageIsReadOnlyForTheNewestEventOfEachProviderAndId()
    {
        var result = Source.Read("System", TimeSpan.FromDays(30), 5000, CancellationToken.None);

        Assert.True(result.TryGet(out var batch), result.Error);
        var withMessage = batch.Events.Where(e => e.Message is not null).GroupBy(e => (e.Provider, e.Id));
        Assert.All(withMessage, g => Assert.Single(g));
    }

    [Fact]
    public void ALimitStopsTheReadAndSaysSo()
    {
        var result = Source.Read("Application", TimeSpan.FromDays(30), 3, CancellationToken.None);

        Assert.True(result.TryGet(out var batch), result.Error);
        if (batch.Truncated)
        {
            Assert.Equal(3, batch.Events.Count);
        }
        else
        {
            Assert.True(batch.Events.Count <= 3);
        }
    }

    [Fact]
    public void ALogThatDoesNotExistIsAFailureNotACrash()
    {
        var result = Source.Read("M1vClock-Log-Que-Nao-Existe", TimeSpan.FromDays(1), 10, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void ACancelledReadStopsInsteadOfFinishing()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => Source.Read("System", TimeSpan.FromDays(30), 5000, cancelled.Token));
    }

    [Fact]
    public async Task TheWholeServiceRunsAgainstThisPcAndEveryGroupIsConsistent()
    {
        var service = new EventService(Source, NullLogger<EventService>.Instance);

        var report = await service.ReadAsync(TimeSpan.FromDays(14), CancellationToken.None);

        Assert.Empty(report.FailedLogs);
        Assert.All(report.Groups, g =>
        {
            Assert.True(g.Count >= 1);
            Assert.True(g.First <= g.Last);
            Assert.Equal(g.Known, EventKnowledge.Find(g.Provider, g.Id));
        });
        Assert.Equal(report.Total, report.Groups.Sum(g => g.Count));
    }
}
