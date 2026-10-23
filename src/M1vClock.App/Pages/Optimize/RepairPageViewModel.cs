using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Services;
using M1vClock.Core.Security;
using M1vClock.Optimize.Repair;

namespace M1vClock.App.Pages.Optimize;

public sealed partial class RepairPageViewModel : PageViewModel, IDisposable
{
    private readonly IRepairService _repair;
    private readonly IClipboardService _clipboard;
    private readonly bool _isElevated;
    private CancellationTokenSource? _cancellation;

    public RepairPageViewModel(IRepairService repair, IClipboardService clipboard, ElevationInfo elevation)
        : base(PageCatalog.Find("repair"))
    {
        ArgumentNullException.ThrowIfNull(repair);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(elevation);
        _repair = repair;
        _clipboard = clipboard;
        _isElevated = elevation.IsElevated;
        Cards = [new RepairCardViewModel(RepairCheck.ComponentsQuick), new RepairCardViewModel(RepairCheck.ComponentsFull), new RepairCardViewModel(RepairCheck.SystemFiles)];
    }

    public IReadOnlyList<RepairCardViewModel> Cards { get; }

    public bool NeedsAdministrator => !_isElevated;

    public bool CanRun => _isElevated && !IsRunning;

    public bool HasNotice => Notice.Length > 0;

    public bool ShowHint => IsRunning && !HasFraction;

    public string ProgressHint { get; } = Loc.Get("Opt.Repair.Running.NoProgress");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(ShowHint))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHint))]
    public partial bool HasFraction { get; set; }

    [ObservableProperty]
    public partial double Fraction { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; set; } = string.Empty;

    public override void OnNavigatedFrom() => Cancel();

    public void Dispose()
    {
        Cancel();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    public async Task RunAsync(RepairCardViewModel? card)
    {
        if (card is null || !CanRun)
        {
            return;
        }

        Notice = string.Empty;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        HasFraction = false;
        IsRunning = true;
        Fraction = 0;
        ProgressText = Loc.Format("Opt.Repair.Running", card.Title);
        try
        {
            var result = await _repair.RunAsync(card.Check, new InlineProgress(percent => Show(card, percent)), cancellation.Token).ConfigureAwait(true);
            card.Show(result);
        }
        finally
        {
            _cancellation = null;
            IsRunning = false;
        }
    }

    [RelayCommand]
    public void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    public void Copy(RepairCardViewModel? card)
    {
        if (card is null || !card.HasCommand)
        {
            return;
        }

        Notice = Loc.Get(_clipboard.TrySetText(card.Command) ? "Opt.Repair.Copied" : "Opt.Repair.CopyFailed");
    }

    private void Show(RepairCardViewModel card, int percent)
    {
        HasFraction = true;
        Fraction = Math.Clamp(percent, 0, 100) / 100.0;
        ProgressText = Loc.Format("Opt.Repair.Running.Percent", card.Title, Math.Clamp(percent, 0, 100).ToString(Loc.Culture));
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
