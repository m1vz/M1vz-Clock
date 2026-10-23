using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Dialogs;
using M1vClock.App.Formatting;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Optimize.Cleanup;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Optimize;

public sealed partial class CleanupPageViewModel : HardwarePageViewModel
{
    private readonly ICleanupService _cleanup;
    private readonly DialogService _dialogs;
    private readonly bool _isElevated;
    private readonly ILogger<CleanupPageViewModel> _logger;

    public CleanupPageViewModel(ICleanupService cleanup, DialogService dialogs, AppInfo info, ILogger<CleanupPageViewModel> logger)
        : base(PageCatalog.Find("cleanup"), logger)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(info);
        _cleanup = cleanup;
        _dialogs = dialogs;
        _isElevated = info.IsElevated;
        _logger = logger;
    }

    public ObservableCollection<CleanupRowViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCleaning { get; set; }

    [ObservableProperty]
    public partial string TotalText { get; set; } = "—";

    [ObservableProperty]
    public partial string FilesText { get; set; } = "—";

    [ObservableProperty]
    public partial string PlacesText { get; set; } = "—";

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool CanCleanAll => !IsLoading && !IsCleaning && Items.Any(i => i.CanClean);

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var summary = await _cleanup.ScanAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            Items.Clear();
            foreach (var scan in summary.Scans)
            {
                Items.Add(new CleanupRowViewModel(scan, _isElevated));
            }

            var cleanable = Items.Where(i => i.CanClean).ToList();
            TotalText = Units.Bytes(cleanable.Sum(i => i.Scan.Bytes), Loc.Culture);
            FilesText = cleanable.Sum(i => i.Scan.Files).ToString("N0", Loc.Culture);
            PlacesText = cleanable.Count.ToString("N0", Loc.Culture);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanCleanAll));
        }
    }

    [RelayCommand]
    public Task CleanAsync(CleanupRowViewModel? row) => row is { CanClean: true } ? RunAsync([row]) : Task.CompletedTask;

    [RelayCommand]
    public Task CleanAllAsync() => RunAsync([.. Items.Where(i => i.CanClean)]);

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    partial void OnIsCleaningChanged(bool value) => OnPropertyChanged(nameof(CanCleanAll));

    private async Task RunAsync(IReadOnlyList<CleanupRowViewModel> rows)
    {
        if (rows.Count == 0 || IsCleaning)
        {
            return;
        }

        var request = new DialogRequest(
            Loc.Get("Opt.Cleanup.Confirm.Title"),
            Loc.Get("Opt.Cleanup.Confirm.Message"),
            [.. rows.Select(r => Loc.Format("Opt.Cleanup.Confirm.Line", r.Title, r.SizeText))],
            Loc.Get("Opt.Cleanup.Confirm.Undo"),
            Loc.Get("Opt.Cleanup.Confirm.Button"),
            Loc.Get("Common.Cancel"),
            DialogTone.Destructive);
        if (!await _dialogs.ShowAsync(request).ConfigureAwait(true))
        {
            return;
        }

        Notice = string.Empty;
        Problem = string.Empty;
        IsCleaning = true;
        var deleted = 0;
        var freed = 0L;
        var failed = 0;
        try
        {
            foreach (var row in rows)
            {
                var outcome = await _cleanup.CleanAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
                deleted += outcome.Deleted;
                freed += outcome.FreedBytes;
                failed += outcome.Failed;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.CleanupFailed(_logger, ex);
            Problem = Loc.Format("Opt.Cleanup.Failed", ex.Message);
            return;
        }
        finally
        {
            IsCleaning = false;
        }

        var done = Loc.Format("Opt.Cleanup.Done", deleted.ToString("N0", Loc.Culture), Units.Bytes(freed, Loc.Culture));
        if (failed > 0)
        {
            done += " " + Loc.Format("Opt.Cleanup.Done.Kept", failed.ToString("N0", Loc.Culture));
        }

        Notice = done;
        await RefreshAsync().ConfigureAwait(true);
    }
}
