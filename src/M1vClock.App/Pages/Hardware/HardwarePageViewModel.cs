using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public abstract partial class HardwarePageViewModel : PageViewModel, IDisposable
{
    private readonly ILogger _logger;
    private CancellationTokenSource? _cancellation;

    protected HardwarePageViewModel(PageDescriptor descriptor, ILogger logger)
        : base(descriptor)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    [ObservableProperty]
    public partial string PageError { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UpdatedText { get; set; } = string.Empty;

    public bool HasPageError => !string.IsNullOrEmpty(PageError);

    public override void OnNavigatedTo() => _ = RefreshAsync();

    public override void OnNavigatedFrom() => Cancel();

    public void Dispose()
    {
        Cancel();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        PageError = string.Empty;
        try
        {
            await LoadAsync(cancellation.Token).ConfigureAwait(true);
            UpdatedText = Loc.Format("Hw.Common.Updated", DateTime.Now.ToString("t", Loc.Culture));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.PageLoadFailed(_logger, ex, Descriptor.Id);
            PageError = Loc.Get("Hw.Common.PageError");
        }
    }

    protected abstract Task LoadAsync(CancellationToken cancellationToken);

    partial void OnPageErrorChanged(string value) => OnPropertyChanged(nameof(HasPageError));

    private void Cancel()
    {
        var current = _cancellation;
        _cancellation = null;
        if (current is null)
        {
            return;
        }

        current.Cancel();
        current.Dispose();
    }
}
