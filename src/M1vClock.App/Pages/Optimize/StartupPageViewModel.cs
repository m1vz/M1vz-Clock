using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Dialogs;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Journal;
using M1vClock.Optimize.Startup;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Optimize;

public sealed partial class StartupPageViewModel : HardwarePageViewModel
{
    private readonly IStartupService _startup;
    private readonly DialogService _dialogs;
    private readonly bool _isElevated;

    public StartupPageViewModel(IStartupService startup, DialogService dialogs, AppInfo info, ILogger<StartupPageViewModel> logger)
        : base(PageCatalog.Find("startup"), logger)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(info);
        _startup = startup;
        _dialogs = dialogs;
        _isElevated = info.IsElevated;
    }

    public ObservableCollection<StartupRowViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string TotalText { get; set; } = "—";

    [ObservableProperty]
    public partial string EnabledText { get; set; } = "—";

    [ObservableProperty]
    public partial string DisabledText { get; set; } = "—";

    [ObservableProperty]
    public partial string MissingText { get; set; } = "—";

    [ObservableProperty]
    public partial MetricState MissingState { get; set; }

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool IsEmpty => !IsLoading && Items.Count == 0 && Problem.Length == 0;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var result = await _startup.ReadAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            ShowReport(result);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    public async Task ChangeAsync(StartupRowViewModel? row)
    {
        if (row is null || !row.CanChange)
        {
            return;
        }

        var enable = !row.Entry.Enabled;
        var name = row.Title;
        var prefix = enable ? "Opt.Startup.Enable" : "Opt.Startup.Disable";
        var request = new DialogRequest(
            Loc.Get($"{prefix}.Title"),
            Loc.Format($"{prefix}.Message", name),
            [Loc.Format($"{prefix}.Change", name)],
            Loc.Get("Opt.Startup.Undo"),
            Loc.Get($"{prefix}.Confirm"),
            Loc.Get("Common.Cancel"),
            DialogTone.Neutral);
        if (!await _dialogs.ShowAsync(request).ConfigureAwait(true))
        {
            return;
        }

        Notice = string.Empty;
        Problem = string.Empty;
        var outcome = await _startup.SetEnabledAsync(row.Entry, enable, CancellationToken.None).ConfigureAwait(true);
        if (!outcome.Succeeded)
        {
            Problem = Loc.Format("Opt.Startup.Failed", name, outcome.Entry.Error ?? Loc.Get("Hw.Common.Unknown"));
            return;
        }

        var done = Loc.Format(enable ? "Opt.Startup.Done.Enabled" : "Opt.Startup.Done.Disabled", name);
        if (!outcome.RestorePoint.IsProtected)
        {
            done += " " + Loc.Format("Opt.Startup.NoRestorePoint", outcome.RestorePoint.Detail ?? outcome.RestorePoint.Status.ToString());
        }

        Notice = done;
        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnProblemChanged(string value)
    {
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ShowReport(M1vClock.Core.Results.ReadResult<StartupReport> result)
    {
        Items.Clear();
        if (!result.TryGet(out var report))
        {
            TotalText = EnabledText = DisabledText = MissingText = "—";
            MissingState = MetricState.Neutral;
            Problem = Loc.Format("Opt.Startup.ReadFailed", result.Error ?? Loc.Get("Hw.Common.Unknown"));
            return;
        }

        Problem = string.Empty;
        foreach (var entry in report.Entries)
        {
            Items.Add(new StartupRowViewModel(entry, _isElevated));
        }

        TotalText = report.Entries.Count.ToString("N0", Loc.Culture);
        EnabledText = report.Enabled.ToString("N0", Loc.Culture);
        DisabledText = report.Disabled.ToString("N0", Loc.Culture);
        MissingText = report.Missing.ToString("N0", Loc.Culture);
        MissingState = report.Missing > 0 ? MetricState.Warning : MetricState.Neutral;
    }
}
