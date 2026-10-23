using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Dialogs;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Shell;
using M1vClock.Optimize.Apps;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Optimize;

public sealed partial class AppsPageViewModel : HardwarePageViewModel
{
    private static readonly AppSort[] SortOrder = [AppSort.Size, AppSort.Name, AppSort.Date];

    private readonly IInstalledAppService _apps;
    private readonly IShellLauncher _shell;
    private readonly DialogService _dialogs;
    private readonly ILogger<AppsPageViewModel> _logger;
    private AppReport? _report;

    public AppsPageViewModel(IInstalledAppService apps, IShellLauncher shell, DialogService dialogs, ILogger<AppsPageViewModel> logger)
        : base(PageCatalog.Find("apps"), logger)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(dialogs);
        _apps = apps;
        _shell = shell;
        _dialogs = dialogs;
        _logger = logger;
    }

    public ObservableCollection<AppRowViewModel> Items { get; } = [];

    public IReadOnlyList<string> SortLabels { get; } = [Loc.Get("Opt.Apps.Sort.Size"), Loc.Get("Opt.Apps.Sort.Name"), Loc.Get("Opt.Apps.Sort.Date")];

    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string TotalText { get; set; } = "—";

    [ObservableProperty]
    public partial string SizeText { get; set; } = "—";

    [ObservableProperty]
    public partial string LargestText { get; set; } = "—";

    [ObservableProperty]
    public partial string NoUninstallerText { get; set; } = "—";

    [ObservableProperty]
    public partial string ShowingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    public bool HasShowing => ShowingText.Length > 0;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool IsEmpty => !IsLoading && Items.Count == 0 && Problem.Length == 0;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var result = await _apps.ReadAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.TryGet(out var report))
            {
                _report = null;
                Items.Clear();
                TotalText = SizeText = LargestText = NoUninstallerText = "—";
                Problem = Loc.Format("Opt.Apps.ReadFailed", result.Error ?? Loc.Get("Hw.Common.Unknown"));
                return;
            }

            _report = report;
            Problem = string.Empty;
            TotalText = report.Apps.Count.ToString("N0", Loc.Culture);
            SizeText = Units.Bytes(report.KnownBytes, Loc.Culture);
            LargestText = report.Largest?.SizeBytes is { } largest ? Units.Bytes(largest, Loc.Culture) : "—";
            NoUninstallerText = report.Apps.Count(a => a.Launch is null).ToString("N0", Loc.Culture);
            ShowItems();
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    public async Task UninstallAsync(AppRowViewModel? row)
    {
        if (row?.App.Launch is not { } launch)
        {
            return;
        }

        var name = row.Title;
        var request = new DialogRequest(
            Loc.Format("Opt.Apps.Confirm.Title", name),
            Loc.Get("Opt.Apps.Confirm.Message"),
            [Loc.Format("Opt.Apps.Confirm.Line", name)],
            Loc.Get("Opt.Apps.Confirm.Undo"),
            Loc.Get("Opt.Apps.Confirm.Action"),
            Loc.Get("Common.Cancel"),
            DialogTone.Destructive);
        if (!await _dialogs.ShowAsync(request).ConfigureAwait(true))
        {
            return;
        }

        Notice = string.Empty;
        Problem = string.Empty;
        try
        {
            _shell.Start(launch.FileName, launch.Arguments, Path.GetDirectoryName(launch.FileName));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or COMException)
        {
            AppLog.UninstallLaunchFailed(_logger, ex, name);
            Problem = Loc.Format("Opt.Apps.LaunchFailed", name, ex.Message);
            return;
        }

        Notice = Loc.Format("Opt.Apps.Opened", name);
    }

    partial void OnSortIndexChanged(int value) => ShowItems();

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

        var sort = SortOrder[Math.Clamp(SortIndex, 0, SortOrder.Length - 1)];
        var rows = AppRows.Filter(report.Apps, sort, SearchText, out var matching);
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        ShowingText = matching > rows.Count ? Loc.Format("Opt.Apps.Showing", rows.Count.ToString("N0", Loc.Culture), matching.ToString("N0", Loc.Culture)) : string.Empty;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
