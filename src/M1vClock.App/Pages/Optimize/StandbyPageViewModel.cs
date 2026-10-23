using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Dialogs;
using M1vClock.App.Formatting;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Optimize.Memory;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Optimize;

public sealed partial class StandbyPageViewModel : HardwarePageViewModel
{
    private readonly IMemoryListService _memory;
    private readonly DialogService _dialogs;
    private readonly bool _isElevated;
    private MemoryListSnapshot? _snapshot;
    private bool _readFailed;

    public StandbyPageViewModel(IMemoryListService memory, DialogService dialogs, AppInfo info, ILogger<StandbyPageViewModel> logger)
        : base(PageCatalog.Find("standby"), logger)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(info);
        _memory = memory;
        _dialogs = dialogs;
        _isElevated = info.IsElevated;
    }

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial bool IsPurging { get; set; }

    [ObservableProperty]
    public partial string TotalText { get; set; } = "—";

    [ObservableProperty]
    public partial string InUseText { get; set; } = "—";

    [ObservableProperty]
    public partial string StandbyText { get; set; } = "—";

    [ObservableProperty]
    public partial string FreeText { get; set; } = "—";

    [ObservableProperty]
    public partial string AvailableText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ModifiedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problem { get; set; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    public bool HasProblem => Problem.Length > 0;

    public bool NeedsAdministrator => !_isElevated;

    public bool CanPurge => !IsLoading && !IsPurging && _isElevated && _snapshot is { StandbyBytes: > 0 };

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var result = await _memory.ReadAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            Show(result);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(CanPurge));
        }
    }

    [RelayCommand]
    public async Task PurgeAsync()
    {
        if (!CanPurge || _snapshot is not { } snapshot)
        {
            return;
        }

        var request = new DialogRequest(
            Loc.Get("Opt.Memory.Confirm.Title"),
            Loc.Get("Opt.Memory.Confirm.Message"),
            [Loc.Format("Opt.Memory.Confirm.Line", Units.Bytes(snapshot.StandbyBytes, Loc.Culture))],
            Loc.Get("Opt.Memory.Confirm.Undo"),
            Loc.Get("Opt.Memory.Confirm.Button"),
            Loc.Get("Common.Cancel"),
            DialogTone.Neutral);
        if (!await _dialogs.ShowAsync(request).ConfigureAwait(true))
        {
            return;
        }

        Notice = string.Empty;
        Problem = string.Empty;
        IsPurging = true;
        PurgeResult result;
        try
        {
            result = await _memory.PurgeStandbyAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally
        {
            IsPurging = false;
        }

        switch (result.Status)
        {
            case PurgeStatus.Done:
                Notice = result.Freed <= 0
                    ? Loc.Get("Opt.Memory.Done.Nothing")
                    : Loc.Format("Opt.Memory.Done", Units.Bytes(result.Freed, Loc.Culture), Units.Bytes(result.StandbyBefore, Loc.Culture), Units.Bytes(result.StandbyAfter, Loc.Culture));
                break;
            case PurgeStatus.NeedsAdministrator:
                Problem = Loc.Get("Opt.Memory.NotAllowed");
                break;
            default:
                Problem = Loc.Format("Opt.Memory.Failed", result.Error ?? Loc.Get("Hw.Common.Unknown"));
                break;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    partial void OnIsPurgingChanged(bool value) => OnPropertyChanged(nameof(CanPurge));

    private void Show(M1vClock.Core.Results.ReadResult<MemoryListSnapshot> result)
    {
        if (!result.TryGet(out var snapshot))
        {
            _snapshot = null;
            TotalText = InUseText = StandbyText = FreeText = "—";
            AvailableText = ModifiedText = string.Empty;
            _readFailed = true;
            Problem = Loc.Format("Opt.Memory.ReadFailed", result.Error ?? Loc.Get("Hw.Common.Unknown"));
            return;
        }

        _snapshot = snapshot;
        if (_readFailed)
        {
            _readFailed = false;
            Problem = string.Empty;
        }

        TotalText = Units.Bytes(snapshot.TotalBytes, Loc.Culture);
        InUseText = Units.Bytes(snapshot.InUseBytes, Loc.Culture);
        StandbyText = Units.Bytes(snapshot.StandbyBytes, Loc.Culture);
        FreeText = Units.Bytes(snapshot.FreeBytes, Loc.Culture);
        AvailableText = Loc.Format("Opt.Memory.Available", Units.Bytes(snapshot.AvailableBytes, Loc.Culture));
        ModifiedText = Loc.Format("Opt.Memory.Modified", Units.Bytes(snapshot.ModifiedBytes, Loc.Culture));
    }
}
