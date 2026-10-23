using System.Globalization;
using System.Security;
using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace M1vClock.Optimize.Tweaks;

public sealed class TweakService : ITweakService
{
    private readonly IReadOnlyList<TweakDefinition> _tweaks;
    private readonly IRegistryAccess _registry;
    private readonly IPowerSchemeAccess _power;
    private readonly INativeMouse _mouse;
    private readonly IChangeJournal _journal;
    private readonly ILogger<TweakService> _logger;

    public TweakService(
        IReadOnlyList<TweakDefinition> tweaks,
        IRegistryAccess registry,
        IPowerSchemeAccess power,
        INativeMouse mouse,
        IChangeJournal journal,
        ILogger<TweakService> logger)
    {
        ArgumentNullException.ThrowIfNull(tweaks);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(mouse);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(logger);
        _tweaks = tweaks;
        _registry = registry;
        _power = power;
        _mouse = mouse;
        _journal = journal;
        _logger = logger;
    }

    public TweakService(
        IRegistryAccess registry,
        IPowerSchemeAccess power,
        INativeMouse mouse,
        IChangeJournal journal,
        ILogger<TweakService> logger)
        : this(TweakCatalog.Create(), registry, power, mouse, journal, logger)
    {
    }

    public Task<ReadResult<TweakReport>> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => ReadResult.Success(Read(cancellationToken)), cancellationToken);

    public TweakPlan? Plan(string id, bool turnOn)
    {
        var tweak = Find(id);
        var changes = ChangesFor(tweak, turnOn, Context());
        if (changes.Count == 0)
        {
            return null;
        }

        return new TweakPlan(
            tweak.Id,
            turnOn,
            tweak.NeedsRestart,
            changes.Any(c => c.NeedsAdministrator),
            changes.Select(c => new TweakChangePreview(c.Where, c.From, c.To)).ToList());
    }

    public async Task<TweakOutcome> SetAsync(string id, bool turnOn, CancellationToken cancellationToken)
    {
        var tweak = Find(id);
        var context = Context();
        var changes = ChangesFor(tweak, turnOn, context);
        if (changes.Count == 0)
        {
            return new TweakOutcome(true, StateOf(tweak, context) ?? TweakState.Unavailable, null, false, false, true, null);
        }

        var applied = new List<JournalEntry>();
        var protectedByPoint = true;
        string? detail = null;
        foreach (var change in changes)
        {
            var outcome = await _journal.ApplyAsync(change.Change, cancellationToken).ConfigureAwait(false);
            if (!outcome.RestorePoint.IsProtected)
            {
                protectedByPoint = false;
                detail ??= outcome.RestorePoint.Detail ?? outcome.RestorePoint.Status.ToString();
            }

            if (!outcome.Succeeded)
            {
                foreach (var entry in applied.AsEnumerable().Reverse())
                {
                    _journal.Undo(entry.Id);
                }

                if (applied.Count > 0)
                {
                    TweakLog.RolledBack(_logger, tweak.Id);
                }

                return TweakOutcome.Failure(StateOf(tweak, Context()) ?? TweakState.Unavailable, outcome.Entry.Error ?? string.Empty);
            }

            applied.Add(outcome.Entry);
        }

        var liveFailed = tweak.Live == LiveEffect.Mouse && !ApplyMouseNow();
        var fresh = Context();
        return new TweakOutcome(true, StateOf(tweak, fresh) ?? TweakState.Unavailable, null, tweak.NeedsRestart, liveFailed, protectedByPoint, detail);
    }

    public Task<TweakRestoreOutcome> RestoreOriginalsAsync(CancellationToken cancellationToken) =>
        Task.Run(() => RestoreOriginals(cancellationToken), cancellationToken);

    private TweakRestoreOutcome RestoreOriginals(CancellationToken cancellationToken)
    {
        var undone = 0;
        var failures = new List<string>();
        var touchedMouse = false;
        var mouseTargets = _tweaks.Where(t => t.Live == LiveEffect.Mouse).SelectMany(t => t.Steps).Select(s => s.Target).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in _journal.Entries.Where(e => e.Module == TweakCatalog.Module && e.CanUndo).OrderByDescending(e => e.Sequence).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = _journal.Undo(entry.Id);
            if (outcome.Status == UndoStatus.Undone)
            {
                undone++;
                touchedMouse |= mouseTargets.Contains(entry.Target);
            }
            else
            {
                failures.Add($"{entry.Title}: {outcome.Error ?? outcome.Status.ToString()}");
            }
        }

        if (touchedMouse && !ApplyMouseNow())
        {
            failures.Add(TweakCatalog.MouseKey);
        }

        return new TweakRestoreOutcome(undone, failures);
    }

    private TweakReport Read(CancellationToken cancellationToken)
    {
        var context = Context();
        var entries = new List<TweakEntry>(_tweaks.Count);
        foreach (var tweak in _tweaks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(Describe(tweak, context));
        }

        return new TweakReport(entries);
    }

    private TweakEntry Describe(TweakDefinition tweak, TweakContext context)
    {
        var targets = tweak.Steps.Select(s => s.Target).ToHashSet(StringComparer.Ordinal);
        var hasOriginal = context.Journal.Any(e => e.Module == TweakCatalog.Module && e.CanUndo && targets.Contains(e.Target));
        try
        {
            var issue = tweak.Steps.Select(s => s.IssueFor(context)).FirstOrDefault(i => i != TweakIssue.None);
            var state = StateOf(tweak, context);
            if (issue == TweakIssue.NoHighPerformancePlan || state is null)
            {
                return new TweakEntry(tweak.Id, TweakState.Unavailable, issue == TweakIssue.None ? TweakIssue.ReadFailed : issue, tweak.NeedsRestart, tweak.NeedsAdministrator, false, hasOriginal);
            }

            var canChange = ChangesFor(tweak, state != TweakState.On, context).Count > 0;
            return new TweakEntry(tweak.Id, state.Value, issue, tweak.NeedsRestart, tweak.NeedsAdministrator, canChange, hasOriginal);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            TweakLog.ReadFailed(_logger, ex, tweak.Id);
            return new TweakEntry(tweak.Id, TweakState.Unavailable, TweakIssue.ReadFailed, tweak.NeedsRestart, tweak.NeedsAdministrator, false, hasOriginal);
        }
    }

    private static TweakState? StateOf(TweakDefinition tweak, TweakContext context)
    {
        var votes = tweak.Steps.Where(s => s.Decides).Select(s => s.IsOn(context)).ToList();
        if (votes.Count == 0 || votes.Any(v => v is null))
        {
            return null;
        }

        if (votes.All(v => v == true))
        {
            return TweakState.On;
        }

        return votes.All(v => v == false) ? TweakState.Off : TweakState.Mixed;
    }

    private static List<TweakStepChange> ChangesFor(TweakDefinition tweak, bool turnOn, TweakContext context)
    {
        var title = (turnOn ? "Ligar ajuste: " : "Desligar ajuste: ") + tweak.JournalTitle;
        return tweak.Steps.Select(s => s.ChangeFor(turnOn, title, context)).OfType<TweakStepChange>().ToList();
    }

    private TweakDefinition Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _tweaks.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Ajuste desconhecido: {id}.", nameof(id));
    }

    private TweakContext Context() => new(_registry, _power, _journal.Entries);

    private bool ApplyMouseNow()
    {
        var address = (string name) => new RegistryValueAddress(RegistryHive.CurrentUser, RegistryView.Registry64, TweakCatalog.MouseKey, name);
        var speed = ReadInt(address("MouseSpeed"));
        var first = ReadInt(address("MouseThreshold1"));
        var second = ReadInt(address("MouseThreshold2"));
        if (speed is null || first is null || second is null || !_mouse.Apply(new MouseAcceleration(first.Value, second.Value, speed.Value)))
        {
            TweakLog.MouseLiveFailed(_logger);
            return false;
        }

        return true;
    }

    private int? ReadInt(RegistryValueAddress address)
    {
        var state = _registry.Read(address);
        return state.ValueExists && int.TryParse(state.Data as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
