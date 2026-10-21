using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Dialogs;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.Core.Journal;

namespace M1vClock.App.Pages;

public sealed partial class JournalEntryViewModel(JournalEntry entry, System.Windows.Input.ICommand undo) : ObservableObject
{
    public JournalEntry Entry { get; } = entry;

    public string Title => Entry.Title;

    public string Detail => $"{Entry.Module} · {Entry.TargetDisplay}";

    public string When => Entry.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Loc.Culture);

    public string StatusText => Loc.Get($"History.Status.{Entry.Status}");

    public MetricState Tone => Entry.Status switch
    {
        ChangeStatus.Applied => MetricState.Good,
        ChangeStatus.Pending => MetricState.Warning,
        ChangeStatus.ApplyFailed or ChangeStatus.UndoFailed => MetricState.Critical,
        _ => MetricState.Neutral,
    };

    public bool CanUndo => Entry.CanUndo;

    public string? Error => Entry.Error;

    public System.Windows.Input.ICommand UndoCommand { get; } = undo;
}

public sealed partial class HistoryPageViewModel : PageViewModel, IDisposable
{
    private readonly IChangeJournal _journal;
    private readonly DialogService _dialogs;

    public HistoryPageViewModel(IChangeJournal journal, DialogService dialogs)
        : base(PageCatalog.Find("history"))
    {
        ArgumentNullException.ThrowIfNull(journal);
        _journal = journal;
        _dialogs = dialogs;
        _journal.Changed += OnJournalChanged;
        Load();
    }

    public ObservableCollection<JournalEntryViewModel> Entries { get; } = [];

    public bool IsEmpty => Entries.Count == 0;

    public bool HasUndoable => Entries.Any(e => e.CanUndo);

    [ObservableProperty]
    public partial string Feedback { get; set; } = string.Empty;

    public void Dispose() => _journal.Changed -= OnJournalChanged;

    [RelayCommand]
    private async Task UndoAsync(JournalEntryViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var outcome = _journal.Undo(item.Entry.Id);
        Feedback = outcome.Status switch
        {
            UndoStatus.Undone => Loc.Format("History.Feedback.Undone", item.Title),
            UndoStatus.BlockedByNewerChange => Loc.Format("History.Feedback.Blocked", outcome.BlockingEntries.Count),
            UndoStatus.NotApplied => Loc.Get("History.Feedback.NotApplied"),
            _ => string.Empty,
        };

        if (outcome.Status == UndoStatus.Failed)
        {
            await _dialogs.ShowAsync(new DialogRequest(
                Loc.Get("History.Error.Title"),
                Loc.Format("History.Error.Message", item.Title, outcome.Error),
                [],
                null,
                Loc.Get("Common.Ok"),
                null,
                DialogTone.Error)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task UndoAllAsync(FrameworkElement? anchor)
    {
        var pending = Entries.Where(e => e.CanUndo).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var request = new DialogRequest(
            Loc.Get("History.UndoAll.Title"),
            Loc.Format("History.UndoAll.Message", pending.Count),
            pending.Take(6).Select(e => $"{e.Title} · {e.Entry.TargetDisplay}").ToList(),
            Loc.Get("History.UndoAll.Undo"),
            Loc.Get("History.UndoAll.Confirm"),
            Loc.Get("Common.Cancel"),
            DialogTone.Destructive);
        if (!await _dialogs.ShowAsync(request, anchor).ConfigureAwait(true))
        {
            return;
        }

        var outcome = _journal.UndoAll();
        Feedback = outcome.Failures.Count == 0
            ? Loc.Format("History.Feedback.UndoneAll", outcome.Undone)
            : Loc.Format("History.Feedback.UndoneSome", outcome.Undone, outcome.Failures.Count);
    }

    private void OnJournalChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            Load();
        }
        else
        {
            dispatcher.BeginInvoke(Load);
        }
    }

    private void Load()
    {
        Entries.Clear();
        foreach (var entry in _journal.Entries)
        {
            Entries.Add(new JournalEntryViewModel(entry, UndoCommand));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasUndoable));
    }
}
