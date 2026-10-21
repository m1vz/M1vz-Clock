using M1vClock.Core.RestorePoints;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Core.Tests;

public sealed class RestorePointGateTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeRestorePointService _service = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task FirstChangeOfTheDayCreatesRestorePoint()
    {
        using var gate = CreateGate();

        var result = await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RestorePointGateStatus.CreatedNow, result.Status);
        Assert.True(result.IsProtected);
        Assert.Equal(1, _service.Created);
    }

    [Fact]
    public async Task LaterChangesOnTheSameDayDoNotCreateAnother()
    {
        using var gate = CreateGate();
        await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromHours(5));

        var result = await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RestorePointGateStatus.AlreadyCreatedToday, result.Status);
        Assert.Equal(1, _service.Created);
    }

    [Fact]
    public async Task NextDayCreatesAgainEvenAfterRestart()
    {
        using (var gate = CreateGate())
        {
            await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);
        }

        _time.Advance(TimeSpan.FromDays(1));
        using var reopened = CreateGate();
        var result = await reopened.EnsureForTodayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RestorePointGateStatus.CreatedNow, result.Status);
        Assert.Equal(2, _service.Created);
    }

    [Fact]
    public async Task DisabledProtectionIsReportedAndRetriedLater()
    {
        _service.Availability = RestorePointAvailability.Disabled;
        using var gate = CreateGate();

        var first = await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);
        _service.Availability = RestorePointAvailability.Enabled;
        var second = await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RestorePointGateStatus.ProtectionDisabled, first.Status);
        Assert.False(first.IsProtected);
        Assert.Equal(RestorePointGateStatus.CreatedNow, second.Status);
    }

    [Fact]
    public async Task RecentWindowsPointCountsAsProtectedForToday()
    {
        _service.Creation = new RestorePointCreation(RestorePointCreationStatus.SkippedRecentPointExists, 0);
        using var gate = CreateGate();

        var first = await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);
        var second = await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RestorePointGateStatus.RecentPointExists, first.Status);
        Assert.Equal(RestorePointGateStatus.AlreadyCreatedToday, second.Status);
        Assert.Equal(1, _service.Created);
    }

    [Fact]
    public async Task FailureIsReportedWithCodeAndRetried()
    {
        _service.Creation = new RestorePointCreation(RestorePointCreationStatus.Failed, 5);
        using var gate = CreateGate();

        var first = await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);
        await gate.EnsureForTodayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RestorePointGateStatus.Failed, first.Status);
        Assert.Equal("código 5", first.Detail);
        Assert.Equal(2, _service.Created);
    }

    private RestorePointGate CreateGate() =>
        new(_service, _temp.File("restore-point.json"), _time, NullLogger<RestorePointGate>.Instance);
}
