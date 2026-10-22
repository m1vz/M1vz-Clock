using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Core.Paths;
using M1vClock.Core.Shell;
using M1vClock.Diagnostics.Logs;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Diagnostics;

public sealed partial class LogsPageViewModel : HardwarePageViewModel
{
    public const int FilesShown = 7;

    private readonly ILogService _logs;
    private readonly IShellLauncher _shell;
    private readonly IClipboardService _clipboard;
    private readonly AppPaths _paths;
    private readonly TimeProvider _clock;
    private List<LogFile> _files = [];
    private LogSlice? _slice;
    private bool _loading;

    public LogsPageViewModel(
        ILogService logs,
        IShellLauncher shell,
        IClipboardService clipboard,
        AppPaths paths,
        TimeProvider clock,
        ILogger<LogsPageViewModel> logger)
        : base(PageCatalog.Find("logs"), logger)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(clock);
        _logs = logs;
        _shell = shell;
        _clipboard = clipboard;
        _paths = paths;
        _clock = clock;
    }

    public ObservableCollection<LogEntryViewModel> Entries { get; } = [];

    public IReadOnlyList<string> LevelLabels { get; } =
    [
        Loc.Get("Diag.Logs.Level.All"),
        Loc.Get("Diag.Logs.Level.Problems"),
        Loc.Get("Diag.Logs.Level.Errors"),
    ];

    [ObservableProperty]
    public partial IReadOnlyList<string> FileLabels { get; set; } = [];

    [ObservableProperty]
    public partial int FileIndex { get; set; }

    [ObservableProperty]
    public partial int LevelIndex { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string SummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ShowingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problems { get; set; } = string.Empty;

    public bool HasFiles => FileLabels.Count > 0;

    public bool HasShowing => ShowingText.Length > 0;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblems => Problems.Length > 0;

    public bool ShowNoFiles => !IsLoading && FileLabels.Count == 0 && Problems.Length == 0;

    public bool ShowEmpty => !IsLoading && FileLabels.Count > 0 && Entries.Count == 0;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        _loading = true;
        try
        {
            var listed = await _logs.ListAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!listed.TryGet(out var files))
            {
                Fail(Loc.Format("Diag.Logs.Failed", listed.Error ?? Loc.Get("Hw.Common.Unknown")));
                return;
            }

            _files = files.Take(FilesShown).ToList();
            var now = _clock.GetUtcNow();
            FileLabels = _files.Select(f => LogRows.FileLabel(f, now)).ToList();
            FileIndex = _files.Count == 0 ? -1 : Math.Clamp(FileIndex, 0, _files.Count - 1);
            if (_files.Count == 0)
            {
                _slice = null;
                Problems = string.Empty;
                ShowEntries();
                return;
            }

            var read = await _logs.ReadAsync(_files[FileIndex], cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!read.TryGet(out var slice))
            {
                Fail(Loc.Format("Diag.Logs.Failed", read.Error ?? Loc.Get("Hw.Common.Unknown")));
                return;
            }

            _slice = slice;
            Problems = string.Empty;
            ShowEntries();
        }
        finally
        {
            _loading = false;
            IsLoading = false;
            NotifyFlags();
        }
    }

    [RelayCommand]
    public void OpenFolder() => _shell.OpenFolder(_paths.Logs);

    [RelayCommand]
    public void Copy()
    {
        Notice = _clipboard.TrySetText(LogRows.PlainText(Entries)) ? Loc.Get("Diag.Logs.Copied") : Loc.Get("Diag.Logs.CopyFailed");
    }

    partial void OnFileIndexChanged(int value)
    {
        if (!_loading && value >= 0)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnLevelIndexChanged(int value) => ShowEntries();

    partial void OnSearchTextChanged(string value) => ShowEntries();

    partial void OnFileLabelsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasFiles));

    partial void OnShowingTextChanged(string value) => OnPropertyChanged(nameof(HasShowing));

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnProblemsChanged(string value) => OnPropertyChanged(nameof(HasProblems));

    private void Fail(string message)
    {
        _slice = null;
        FileLabels = [];
        Problems = message;
        ShowEntries();
    }

    private void ShowEntries()
    {
        Entries.Clear();
        Notice = string.Empty;
        if (_slice is not { } slice)
        {
            SummaryText = string.Empty;
            ShowingText = string.Empty;
            NotifyFlags();
            return;
        }

        var shown = LogRows.Filter(slice.Entries, LevelIndex, SearchText, out var matching);
        foreach (var row in shown)
        {
            Entries.Add(row);
        }

        SummaryText = LogRows.Summary(slice.Entries);
        ShowingText = matching > shown.Count ? Loc.Format("Diag.Logs.Showing", shown.Count.ToString("N0", Loc.Culture), matching.ToString("N0", Loc.Culture)) : string.Empty;
        if (slice.StartsMidFile)
        {
            Notice = Loc.Get("Diag.Logs.MidFile");
        }

        NotifyFlags();
    }

    private void NotifyFlags()
    {
        OnPropertyChanged(nameof(ShowNoFiles));
        OnPropertyChanged(nameof(ShowEmpty));
    }
}
