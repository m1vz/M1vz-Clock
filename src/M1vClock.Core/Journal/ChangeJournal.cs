using System.Text.Json.Nodes;
using M1vClock.Core.RestorePoints;
using Microsoft.Extensions.Logging;

namespace M1vClock.Core.Journal;

public sealed class ChangeJournal : IChangeJournal, IDisposable
{
    private readonly JournalStore _store;
    private readonly Dictionary<string, IChangeHandler> _handlers;
    private readonly IRestorePointGate _restorePoints;
    private readonly TimeProvider _time;
    private readonly ILogger<ChangeJournal> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<JournalEntry> _entries;

    public ChangeJournal(
        JournalStore store,
        IEnumerable<IChangeHandler> handlers,
        IRestorePointGate restorePoints,
        TimeProvider time,
        ILogger<ChangeJournal> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(restorePoints);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _handlers = handlers.ToDictionary(h => h.Kind, StringComparer.Ordinal);
        _restorePoints = restorePoints;
        _time = time;
        _logger = logger;
        _entries = [.. store.Load()];
    }

    public event EventHandler? Changed;

    public IReadOnlyList<JournalEntry> Entries
    {
        get
        {
            _gate.Wait();
            try
            {
                return _entries.OrderByDescending(e => e.Sequence).ToList();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task<ApplyOutcome> ApplyAsync(SystemChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var handler = HandlerFor(change.Kind);
        var restorePoint = await _restorePoints.EnsureForTodayAsync(cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = handler.Read(change.Address);
            var entry = new JournalEntry
            {
                Id = Guid.NewGuid(),
                Sequence = _entries.Count == 0 ? 1 : _entries.Max(e => e.Sequence) + 1,
                CreatedAt = _time.GetUtcNow(),
                Kind = change.Kind,
                Module = change.Module,
                Title = change.Title,
                Target = change.Target,
                TargetDisplay = change.TargetDisplay,
                Address = change.Address.DeepClone().AsObject(),
                Before = before,
                Desired = change.DesiredState.DeepClone().AsObject(),
                Status = ChangeStatus.Pending,
            };
            Upsert(entry);

            entry = ApplyPending(entry, handler);
            Upsert(entry);
            CoreLog.ChangeRecorded(_logger, entry.Status, entry.Title, entry.TargetDisplay, entry.Module);
            return new ApplyOutcome(entry, restorePoint);
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public UndoOutcome Undo(Guid entryId)
    {
        _gate.Wait();
        UndoOutcome outcome;
        try
        {
            outcome = UndoLocked(entryId);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return outcome;
    }

    public UndoAllOutcome UndoAll()
    {
        _gate.Wait();
        var undone = 0;
        var failures = new List<UndoOutcome>();
        try
        {
            foreach (var entry in _entries.Where(e => e.CanUndo).OrderByDescending(e => e.Sequence).ToList())
            {
                var outcome = UndoLocked(entry.Id);
                if (outcome.Status == UndoStatus.Undone)
                {
                    undone++;
                }
                else
                {
                    failures.Add(outcome);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new UndoAllOutcome(undone, failures);
    }

    private JournalEntry ApplyPending(JournalEntry entry, IChangeHandler handler)
    {
        try
        {
            handler.Write(entry.Address, entry.Desired);
            var after = handler.Read(entry.Address);
            if (handler.StatesMatch(after, entry.Desired))
            {
                return entry with { Status = ChangeStatus.Applied, After = after };
            }

            CoreLog.ChangeNotKept(_logger, entry.Title, entry.TargetDisplay);
            RestoreAfterFailure(entry, handler);
            return entry with { Status = ChangeStatus.ApplyFailed, After = after, Error = "O Windows não aceitou a alteração." };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CoreLog.ChangeApplyFailed(_logger, ex, entry.Title, entry.TargetDisplay);
            RestoreAfterFailure(entry, handler);
            return entry with { Status = ChangeStatus.ApplyFailed, Error = ex.Message };
        }
    }

    private void RestoreAfterFailure(JournalEntry entry, IChangeHandler handler)
    {
        try
        {
            handler.Write(entry.Address, entry.Before);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CoreLog.ChangeRollbackFailed(_logger, ex, entry.TargetDisplay);
        }
    }

    private UndoOutcome UndoLocked(Guid entryId)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null)
        {
            return new UndoOutcome(UndoStatus.NotFound, null, [], null);
        }

        if (!entry.CanUndo)
        {
            return new UndoOutcome(UndoStatus.NotApplied, entry, [], null);
        }

        var blocking = _entries
            .Where(e => e.CanUndo && e.Id != entry.Id && e.Target == entry.Target && e.Sequence > entry.Sequence)
            .OrderByDescending(e => e.Sequence)
            .ToList();
        if (blocking.Count > 0)
        {
            return new UndoOutcome(UndoStatus.BlockedByNewerChange, entry, blocking, null);
        }

        var handler = HandlerFor(entry.Kind);
        try
        {
            handler.Write(entry.Address, entry.Before);
            var current = handler.Read(entry.Address);
            if (!handler.StatesMatch(current, entry.Before))
            {
                var failed = entry with { Status = ChangeStatus.UndoFailed, Error = "O valor lido depois de desfazer é diferente do original." };
                Upsert(failed);
                CoreLog.UndoNotVerified(_logger, entry.Title, entry.TargetDisplay);
                return new UndoOutcome(UndoStatus.Failed, failed, [], failed.Error);
            }

            var undone = entry with { Status = ChangeStatus.Undone, UndoneAt = _time.GetUtcNow(), Error = null };
            Upsert(undone);
            CoreLog.ChangeUndone(_logger, entry.Title, entry.TargetDisplay);
            return new UndoOutcome(UndoStatus.Undone, undone, [], null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            CoreLog.UndoFailed(_logger, ex, entry.Title, entry.TargetDisplay);
            var failed = entry with { Status = ChangeStatus.UndoFailed, Error = ex.Message };
            Upsert(failed);
            return new UndoOutcome(UndoStatus.Failed, failed, [], ex.Message);
        }
    }

    public void Dispose() => _gate.Dispose();

    private IChangeHandler HandlerFor(string kind) =>
        _handlers.TryGetValue(kind, out var handler)
            ? handler
            : throw new InvalidOperationException($"Nenhum tratador registrado para alterações do tipo {kind}.");

    private void Upsert(JournalEntry entry)
    {
        var index = _entries.FindIndex(e => e.Id == entry.Id);
        if (index >= 0)
        {
            _entries[index] = entry;
        }
        else
        {
            _entries.Add(entry);
        }

        _store.Save(_entries);
    }
}
