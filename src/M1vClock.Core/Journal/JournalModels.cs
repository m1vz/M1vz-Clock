using System.Text.Json.Nodes;
using M1vClock.Core.RestorePoints;

namespace M1vClock.Core.Journal;

public enum ChangeStatus
{
    Pending,
    Applied,
    ApplyFailed,
    Undone,
    UndoFailed,
}

public sealed record SystemChange(
    string Kind,
    string Module,
    string Title,
    string Target,
    string TargetDisplay,
    JsonObject Address,
    JsonObject DesiredState,
    bool AffectsSystem = true);

public sealed record JournalEntry
{
    public required Guid Id { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required string Kind { get; init; }

    public required string Module { get; init; }

    public required string Title { get; init; }

    public required string Target { get; init; }

    public required string TargetDisplay { get; init; }

    public required JsonObject Address { get; init; }

    public required JsonObject Before { get; init; }

    public required JsonObject Desired { get; init; }

    public JsonObject? After { get; init; }

    public required ChangeStatus Status { get; init; }

    public DateTimeOffset? UndoneAt { get; init; }

    public string? Error { get; init; }

    public bool CanUndo => Status is ChangeStatus.Applied or ChangeStatus.Pending;
}

public interface IChangeHandler
{
    string Kind { get; }

    JsonObject Read(JsonObject address);

    void Write(JsonObject address, JsonObject state);

    bool StatesMatch(JsonObject actual, JsonObject expected) => JsonNode.DeepEquals(actual, expected);
}

public sealed record ApplyOutcome(JournalEntry Entry, RestorePointGateResult RestorePoint)
{
    public bool Succeeded => Entry.Status == ChangeStatus.Applied;
}

public enum UndoStatus
{
    Undone,
    NotFound,
    NotApplied,
    BlockedByNewerChange,
    Failed,
}

public sealed record UndoOutcome(UndoStatus Status, JournalEntry? Entry, IReadOnlyList<JournalEntry> BlockingEntries, string? Error);

public sealed record UndoAllOutcome(int Undone, IReadOnlyList<UndoOutcome> Failures);

public interface IChangeJournal
{
    event EventHandler? Changed;

    IReadOnlyList<JournalEntry> Entries { get; }

    Task<ApplyOutcome> ApplyAsync(SystemChange change, CancellationToken cancellationToken);

    UndoOutcome Undo(Guid entryId);

    UndoAllOutcome UndoAll();
}
