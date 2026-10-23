using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Dialogs;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Security;
using M1vClock.Optimize.Tweaks;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Optimize;

public sealed partial class TweaksPageViewModel : HardwarePageViewModel
{
    private readonly ITweakService _tweaks;
    private readonly DialogService _dialogs;
    private readonly bool _isElevated;
    private TweakReport? _report;

    public TweaksPageViewModel(ITweakService tweaks, DialogService dialogs, ElevationInfo elevation, ILogger<TweaksPageViewModel> logger)
        : base(PageCatalog.Find("tweaks"), logger)
    {
        ArgumentNullException.ThrowIfNull(tweaks);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(elevation);
        _tweaks = tweaks;
        _dialogs = dialogs;
        _isElevated = elevation.IsElevated;
        var name = elevation.UserName;
        var separator = name.LastIndexOf('\\');
        UserNote = Loc.Format("Opt.Tweaks.UserNote", separator >= 0 ? name[(separator + 1)..] : name);
    }

    public ObservableCollection<TweakRowViewModel> Items { get; } = [];

    public string UserNote { get; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string TotalText { get; set; } = "—";

    [ObservableProperty]
    public partial string OnText { get; set; } = "—";

    [ObservableProperty]
    public partial string OffText { get; set; } = "—";

    [ObservableProperty]
    public partial string ChangedText { get; set; } = "—";

    [ObservableProperty]
    public partial bool CanRestore { get; set; }

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var result = await _tweaks.ReadAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            Items.Clear();
            if (!result.TryGet(out var report))
            {
                _report = null;
                TotalText = OnText = OffText = ChangedText = "—";
                CanRestore = false;
                Problem = Loc.Format("Opt.Tweaks.ReadFailed", result.Error ?? Loc.Get("Hw.Common.Unknown"));
                return;
            }

            _report = report;
            Problem = string.Empty;
            foreach (var entry in report.Entries)
            {
                Items.Add(new TweakRowViewModel(entry, _isElevated));
            }

            TotalText = report.Entries.Count.ToString("N0", Loc.Culture);
            OnText = report.On.ToString("N0", Loc.Culture);
            OffText = report.Off.ToString("N0", Loc.Culture);
            ChangedText = report.ChangedByThisApp.ToString("N0", Loc.Culture);
            CanRestore = report.ChangedByThisApp > 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ChangeAsync(TweakRowViewModel? row)
    {
        if (row is null || !row.CanChange)
        {
            return;
        }

        var turnOn = row.TurnOn;
        var plan = _tweaks.Plan(row.Id, turnOn);
        if (plan is null)
        {
            await RefreshAsync().ConfigureAwait(true);
            return;
        }

        var lines = plan.Changes
            .Select(c => Loc.Format("Opt.Tweaks.Change.Line", c.Where, Shown(c.From), Shown(c.To)))
            .ToList();
        var message = Loc.Format("Opt.Tweaks.Change.Message", row.Title);
        if (plan.NeedsRestart)
        {
            message += " " + Loc.Get("Opt.Tweaks.Change.Restart");
        }

        var request = new DialogRequest(
            Loc.Get(turnOn ? "Opt.Tweaks.Change.TitleOn" : "Opt.Tweaks.Change.TitleOff"),
            message,
            lines,
            Loc.Get("Opt.Tweaks.Change.Undo"),
            Loc.Get(turnOn ? "Opt.Tweaks.Change.ConfirmOn" : "Opt.Tweaks.Change.ConfirmOff"),
            Loc.Get("Common.Cancel"),
            DialogTone.Neutral);
        if (!await _dialogs.ShowAsync(request).ConfigureAwait(true))
        {
            return;
        }

        Notice = string.Empty;
        Problem = string.Empty;
        var outcome = await _tweaks.SetAsync(row.Id, turnOn, CancellationToken.None).ConfigureAwait(true);
        if (!outcome.Succeeded)
        {
            Problem = Loc.Format("Opt.Tweaks.Failed", row.Title, string.IsNullOrWhiteSpace(outcome.Error) ? Loc.Get("Hw.Common.Unknown") : outcome.Error);
            return;
        }

        var done = Loc.Format(turnOn ? "Opt.Tweaks.Done.On" : "Opt.Tweaks.Done.Off", row.Title);
        if (outcome.RestartNeeded)
        {
            done += " " + Loc.Get("Opt.Tweaks.Done.Restart");
        }

        if (outcome.LiveFailed)
        {
            done += " " + Loc.Get("Opt.Tweaks.Done.MouseLater");
        }

        if (!outcome.RestorePointProtected)
        {
            done += " " + Loc.Format("Opt.Startup.NoRestorePoint", outcome.RestorePointDetail ?? Loc.Get("Hw.Common.Unknown"));
        }

        Notice = done;
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RestoreAsync()
    {
        if (!CanRestore)
        {
            return;
        }

        var request = new DialogRequest(
            Loc.Get("Opt.Tweaks.Restore.Title"),
            Loc.Get("Opt.Tweaks.Restore.Message"),
            [Loc.Format("Opt.Tweaks.Restore.Line", (_report?.ChangedByThisApp ?? 0).ToString("N0", Loc.Culture))],
            Loc.Get("Opt.Tweaks.Restore.Undo"),
            Loc.Get("Opt.Tweaks.Restore.Confirm"),
            Loc.Get("Common.Cancel"),
            DialogTone.Neutral);
        if (!await _dialogs.ShowAsync(request).ConfigureAwait(true))
        {
            return;
        }

        Notice = string.Empty;
        Problem = string.Empty;
        var outcome = await _tweaks.RestoreOriginalsAsync(CancellationToken.None).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
        if (outcome.Undone > 0)
        {
            Notice = Loc.Format("Opt.Tweaks.Restore.Done", outcome.Undone.ToString("N0", Loc.Culture));
        }

        if (outcome.Failures.Count > 0)
        {
            Problem = Loc.Format("Opt.Tweaks.Restore.Partial", string.Join("; ", outcome.Failures));
        }
    }

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    private static string Shown(string? value) => value ?? Loc.Get("Opt.Tweaks.Value.Absent");
}
