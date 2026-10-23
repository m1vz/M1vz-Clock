using M1vClock.Core.Journal;

namespace M1vClock.Optimize.Tweaks;

public enum TweakState
{
    On,
    Off,
    Mixed,
    Unavailable,
}

public enum TweakIssue
{
    None,
    NoHighPerformancePlan,
    NoPlanToReturnTo,
    ReadFailed,
}

public enum LiveEffect
{
    None,
    Mouse,
}

public sealed record TweakDefinition(string Id, string JournalTitle, bool NeedsRestart, LiveEffect Live, IReadOnlyList<TweakStep> Steps)
{
    public bool NeedsAdministrator => Steps.Any(s => s.NeedsAdministrator);
}

public sealed record TweakEntry(string Id, TweakState State, TweakIssue Issue, bool NeedsRestart, bool NeedsAdministrator, bool CanChange, bool HasOriginalToRestore);

public sealed record TweakReport(IReadOnlyList<TweakEntry> Entries)
{
    public int On => Entries.Count(e => e.State == TweakState.On);

    public int Off => Entries.Count(e => e.State == TweakState.Off);

    public int ChangedByThisApp => Entries.Count(e => e.HasOriginalToRestore);
}

public sealed record TweakChangePreview(string Where, string? From, string? To);

public sealed record TweakPlan(string Id, bool TurnOn, bool NeedsRestart, bool NeedsAdministrator, IReadOnlyList<TweakChangePreview> Changes);

public sealed record TweakOutcome(bool Succeeded, TweakState State, string? Error, bool RestartNeeded, bool LiveFailed, bool RestorePointProtected, string? RestorePointDetail)
{
    public static TweakOutcome Failure(TweakState state, string error) => new(false, state, error, false, false, true, null);
}

public sealed record TweakRestoreOutcome(int Undone, IReadOnlyList<string> Failures);

public interface ITweakService
{
    Task<M1vClock.Core.Results.ReadResult<TweakReport>> ReadAsync(CancellationToken cancellationToken);

    TweakPlan? Plan(string id, bool turnOn);

    Task<TweakOutcome> SetAsync(string id, bool turnOn, CancellationToken cancellationToken);

    Task<TweakRestoreOutcome> RestoreOriginalsAsync(CancellationToken cancellationToken);
}

public sealed record TweakStepChange(SystemChange Change, string Where, string? From, string? To, bool NeedsAdministrator);
