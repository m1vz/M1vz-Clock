using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.RestorePoints;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32;

namespace M1vClock.Core.Tests;

public sealed class ChangeJournalTests : IDisposable
{
    private static readonly RegistryValueAddress GameMode =
        new(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");

    private static readonly RegistryValueAddress GameDvr =
        new(RegistryHive.CurrentUser, RegistryView.Default, @"System\GameConfigStore", "GameDVR_Enabled");

    private readonly TempDirectory _temp = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeRestorePointGate _gate = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ApplyCapturesOriginalStateBeforeChanging()
    {
        _registry.Seed(GameMode, RegistryValueState.DWord(0));
        using var journal = CreateJournal();

        var outcome = await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        Assert.Equal(RegistryValueState.DWord(0).ToJson().ToJsonString(), outcome.Entry.Before.ToJsonString());
        Assert.Equal(1, _registry.Read(GameMode).Data);
        Assert.Equal(1, _gate.Calls);
    }

    [Fact]
    public async Task UndoRestoresOriginalValue()
    {
        _registry.Seed(GameMode, RegistryValueState.DWord(0));
        using var journal = CreateJournal();
        var applied = await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken);

        var undo = journal.Undo(applied.Entry.Id);

        Assert.Equal(UndoStatus.Undone, undo.Status);
        Assert.Equal(0, _registry.Read(GameMode).Data);
        Assert.Equal(ChangeStatus.Undone, journal.Entries.Single().Status);
    }

    [Fact]
    public async Task UndoOfValueThatDidNotExistRemovesIt()
    {
        using var journal = CreateJournal();
        var applied = await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken);

        journal.Undo(applied.Entry.Id);

        Assert.False(_registry.Read(GameMode).ValueExists);
    }

    [Fact]
    public async Task UndoIsBlockedWhileANewerChangeTouchesTheSameTarget()
    {
        _registry.Seed(GameMode, RegistryValueState.DWord(0));
        using var journal = CreateJournal();
        var first = await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken);
        var second = await journal.ApplyAsync(SetGameMode(2), TestContext.Current.CancellationToken);

        var blocked = journal.Undo(first.Entry.Id);

        Assert.Equal(UndoStatus.BlockedByNewerChange, blocked.Status);
        Assert.Equal(second.Entry.Id, Assert.Single(blocked.BlockingEntries).Id);
        Assert.Equal(2, _registry.Read(GameMode).Data);
    }

    [Fact]
    public async Task UndoAllRestoresEverythingInReverseOrder()
    {
        _registry.Seed(GameMode, RegistryValueState.DWord(0));
        _registry.Seed(GameDvr, RegistryValueState.DWord(1));
        using var journal = CreateJournal();
        await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken);
        await journal.ApplyAsync(RegistryValueChanges.Set("Otimização", "Game DVR desligado", GameDvr, RegistryValueState.DWord(0)), TestContext.Current.CancellationToken);
        await journal.ApplyAsync(SetGameMode(2), TestContext.Current.CancellationToken);

        var outcome = journal.UndoAll();

        Assert.Equal(3, outcome.Undone);
        Assert.Empty(outcome.Failures);
        Assert.Equal(0, _registry.Read(GameMode).Data);
        Assert.Equal(1, _registry.Read(GameDvr).Data);
        Assert.All(journal.Entries, e => Assert.Equal(ChangeStatus.Undone, e.Status));
    }

    [Fact]
    public async Task FailedWriteIsRecordedAndOriginalValueIsKept()
    {
        _registry.Seed(GameMode, RegistryValueState.DWord(0));
        _registry.ThrowOnWrite = new UnauthorizedAccessException("Acesso negado.");
        using var journal = CreateJournal();

        var outcome = await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ChangeStatus.ApplyFailed, outcome.Entry.Status);
        Assert.Equal("Acesso negado.", outcome.Entry.Error);
        Assert.Equal(0, _registry.Read(GameMode).Data);
    }

    [Fact]
    public async Task ChangeTheSystemDidNotKeepIsMarkedAsFailed()
    {
        _registry.Seed(GameMode, RegistryValueState.DWord(0));
        _registry.Rejects = (_, state) => state.Data is 1;
        using var journal = CreateJournal();

        var outcome = await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken);

        Assert.Equal(ChangeStatus.ApplyFailed, outcome.Entry.Status);
        Assert.Equal(0, _registry.Read(GameMode).Data);
    }

    [Fact]
    public async Task EntriesSurviveRestartAndCanStillBeUndone()
    {
        _registry.Seed(GameMode, RegistryValueState.DWord(0));
        Guid id;
        using (var journal = CreateJournal())
        {
            id = (await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken)).Entry.Id;
        }

        using var reopened = CreateJournal();
        var undo = reopened.Undo(id);

        Assert.Equal(UndoStatus.Undone, undo.Status);
        Assert.Equal(0, _registry.Read(GameMode).Data);
    }

    [Fact]
    public void PendingEntryFromInterruptedSessionCanBeUndone()
    {
        _registry.Seed(GameMode, RegistryValueState.DWord(1));
        var store = new JournalStore(_temp.File("journal.json"), NullLogger<JournalStore>.Instance);
        var change = SetGameMode(1);
        var pending = new JournalEntry
        {
            Id = Guid.NewGuid(),
            Sequence = 1,
            CreatedAt = _time.GetUtcNow(),
            Kind = change.Kind,
            Module = change.Module,
            Title = change.Title,
            Target = change.Target,
            TargetDisplay = change.TargetDisplay,
            Address = change.Address,
            Before = RegistryValueState.DWord(0).ToJson(),
            Desired = change.DesiredState,
            Status = ChangeStatus.Pending,
        };
        store.Save([pending]);
        using var journal = CreateJournal();

        var undo = journal.Undo(pending.Id);

        Assert.Equal(UndoStatus.Undone, undo.Status);
        Assert.Equal(0, _registry.Read(GameMode).Data);
    }

    [Fact]
    public async Task UndoingTwiceReportsNotApplied()
    {
        using var journal = CreateJournal();
        var applied = await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken);
        journal.Undo(applied.Entry.Id);

        var again = journal.Undo(applied.Entry.Id);

        Assert.Equal(UndoStatus.NotApplied, again.Status);
    }

    [Fact]
    public async Task ChangeThatDoesNotAffectTheSystemSkipsTheRestorePoint()
    {
        using var journal = CreateJournal();
        var change = RegistryValueChanges.Set("Demonstração", "Valor de teste", GameMode, RegistryValueState.DWord(1), affectsSystem: false);

        var outcome = await journal.ApplyAsync(change, TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, _gate.Calls);
        Assert.Equal(RestorePointGateStatus.NotRequired, outcome.RestorePoint.Status);
        Assert.True(outcome.RestorePoint.IsProtected);
    }

    [Fact]
    public async Task ChangeThatAffectsTheSystemStillAsksForTheRestorePoint()
    {
        using var journal = CreateJournal();

        var outcome = await journal.ApplyAsync(SetGameMode(1), TestContext.Current.CancellationToken);

        Assert.Equal(1, _gate.Calls);
        Assert.Equal(RestorePointGateStatus.CreatedNow, outcome.RestorePoint.Status);
    }

    private static SystemChange SetGameMode(int value) =>
        RegistryValueChanges.Set("Otimização", "Modo de Jogo", GameMode, RegistryValueState.DWord(value));

    private ChangeJournal CreateJournal() => new(
        new JournalStore(_temp.File("journal.json"), NullLogger<JournalStore>.Instance),
        [new RegistryValueHandler(_registry)],
        _gate,
        _time,
        NullLogger<ChangeJournal>.Instance);
}
