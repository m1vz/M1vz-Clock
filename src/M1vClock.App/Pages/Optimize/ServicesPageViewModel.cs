using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Dialogs;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Optimize.Services;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Optimize;

public sealed partial class ServicesPageViewModel : HardwarePageViewModel
{
    private readonly IServiceManager _services;
    private readonly DialogService _dialogs;
    private readonly bool _isElevated;
    private ServiceReport? _report;

    public ServicesPageViewModel(IServiceManager services, DialogService dialogs, AppInfo info, ILogger<ServicesPageViewModel> logger)
        : base(PageCatalog.Find("services"), logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(info);
        _services = services;
        _dialogs = dialogs;
        _isElevated = info.IsElevated;
    }

    public ObservableCollection<ServiceRowViewModel> Items { get; } = [];

    public IReadOnlyList<string> FilterLabels { get; } = [Loc.Get("Opt.Services.Filter.ThirdParty"), Loc.Get("Opt.Services.Filter.All")];

    [ObservableProperty]
    public partial int FilterIndex { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string TotalText { get; set; } = "—";

    [ObservableProperty]
    public partial string ThirdPartyText { get; set; } = "—";

    [ObservableProperty]
    public partial string AutoThirdText { get; set; } = "—";

    [ObservableProperty]
    public partial string RunningText { get; set; } = "—";

    [ObservableProperty]
    public partial string ShowingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    public bool HasShowing => ShowingText.Length > 0;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool NeedsAdministrator => !_isElevated;

    public bool IsEmpty => !IsLoading && Items.Count == 0 && Problem.Length == 0;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var result = await _services.ReadAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.TryGet(out var report))
            {
                _report = null;
                Items.Clear();
                TotalText = ThirdPartyText = AutoThirdText = RunningText = "—";
                Problem = Loc.Format("Opt.Services.ReadFailed", result.Error ?? Loc.Get("Hw.Common.Unknown"));
                return;
            }

            _report = report;
            Problem = string.Empty;
            TotalText = report.Total.ToString("N0", Loc.Culture);
            ThirdPartyText = report.ThirdParty.ToString("N0", Loc.Culture);
            AutoThirdText = report.AutomaticThirdParty.ToString("N0", Loc.Culture);
            RunningText = report.Running.ToString("N0", Loc.Culture);
            ShowItems();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    public async Task ChangeAsync(ServiceModeOption? option)
    {
        if (option is null || !option.IsEnabled)
        {
            return;
        }

        var entry = option.Row.Entry;
        var name = option.Row.Title;
        var from = ServiceRows.ModeText(entry.StartMode);
        var to = option.Text;
        var request = new DialogRequest(
            Loc.Format("Opt.Services.Change.Title", name),
            Loc.Format("Opt.Services.Change.Message", name, to),
            [Loc.Format("Opt.Services.Change.Line", name, from, to)],
            Loc.Get("Opt.Services.Change.Undo"),
            Loc.Get("Opt.Services.Change.Confirm"),
            Loc.Get("Common.Cancel"),
            DialogTone.Neutral);
        if (!await _dialogs.ShowAsync(request).ConfigureAwait(true))
        {
            return;
        }

        Notice = string.Empty;
        Problem = string.Empty;
        var outcome = await _services.SetStartModeAsync(entry, option.Mode, CancellationToken.None).ConfigureAwait(true);
        if (!outcome.Succeeded)
        {
            Problem = Loc.Format("Opt.Services.Failed", name, outcome.Entry.Error ?? Loc.Get("Hw.Common.Unknown"));
            return;
        }

        var done = Loc.Format("Opt.Services.Done", name, to);
        if (!outcome.RestorePoint.IsProtected)
        {
            done += " " + Loc.Format("Opt.Startup.NoRestorePoint", outcome.RestorePoint.Detail ?? outcome.RestorePoint.Status.ToString());
        }

        Notice = done;
        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnFilterIndexChanged(int value) => ShowItems();

    partial void OnSearchTextChanged(string value) => ShowItems();

    partial void OnShowingTextChanged(string value) => OnPropertyChanged(nameof(HasShowing));

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnProblemChanged(string value)
    {
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ShowItems()
    {
        Items.Clear();
        if (_report is not { } report)
        {
            ShowingText = string.Empty;
            return;
        }

        var rows = ServiceRows.Filter(report.Services, FilterIndex == 0, SearchText, _isElevated, out var matching);
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        ShowingText = matching > rows.Count ? Loc.Format("Opt.Services.Showing", rows.Count.ToString("N0", Loc.Culture), matching.ToString("N0", Loc.Culture)) : string.Empty;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
