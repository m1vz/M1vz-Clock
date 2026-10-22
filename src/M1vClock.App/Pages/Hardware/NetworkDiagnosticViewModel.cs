using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Localization;
using M1vClock.Hardware;
using M1vClock.Hardware.Network;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed partial class NetworkDiagnosticViewModel : ObservableObject
{
    private readonly INetworkService _network;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cancellation;
    private int _run;

    public NetworkDiagnosticViewModel(INetworkService network, ILogger<NetworkDiagnosticViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(logger);
        _network = network;
        _logger = logger;
        Result = new InfoSectionViewModel(Loc.Get("Hw.Diag.Result"), "Icon.Network");
    }

    public InfoSectionViewModel Result { get; }

    public string ButtonText => Loc.Get(IsRunning ? "Hw.Diag.Cancel" : "Hw.Diag.Start");

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string StageText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasResult { get; set; }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ToggleAsync()
    {
        if (IsRunning)
        {
            Cancel();
            return;
        }

        await RunAsync().ConfigureAwait(true);
    }

    public void Cancel() => _cancellation?.Cancel();

    private async Task RunAsync()
    {
        var run = ++_run;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Message = string.Empty;
        HasResult = false;
        Progress = 0;
        StageText = NetworkRows.Stage(NetworkStage.FindingConnection);
        IsRunning = true;
        try
        {
            var report = await _network.RunTestAsync(new ContextProgress<NetworkProgress>(p => OnProgress(run, p)), cancellation.Token).ConfigureAwait(true);
            Show(report);
        }
        catch (OperationCanceledException)
        {
            Message = Loc.Get("Hw.Diag.Cancelled");
            Progress = 0;
        }
        catch (Exception ex) when (ex is HardwareReadException or InvalidOperationException or IOException)
        {
            AppLog.NetworkDiagnosticFailed(_logger, ex);
            Message = Loc.Get("Hw.Diag.Failed");
            Progress = 0;
        }
        finally
        {
            _cancellation = null;
            IsRunning = false;
            StageText = string.Empty;
        }
    }

    private void OnProgress(int run, NetworkProgress progress)
    {
        if (run != _run || !IsRunning)
        {
            return;
        }

        Progress = progress.Fraction;
        StageText = NetworkRows.Stage(progress.Stage);
    }

    private void Show(NetworkTestReport? report)
    {
        if (report is null)
        {
            Message = Loc.Get("Hw.Net.NoConnection.Note");
            Progress = 0;
            return;
        }

        Progress = 1;
        Result.Subtitle = Loc.Format("Hw.Diag.TestedAt", report.FinishedAt.LocalDateTime.ToString("T", Loc.Culture));
        Result.StatusText = NetworkRows.Verdict(report.Assessment.Verdict);
        Result.StatusTone = NetworkRows.Tone(report.Assessment.Verdict);
        Result.SetReady(NetworkRows.Metrics(report.Measurements));
        Result.SetNotes(NetworkRows.Notes(report.Assessment));
        HasResult = true;
    }

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(ButtonText));

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
}

internal sealed class ContextProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Report(T value)
    {
        if (_context is null)
        {
            handler(value);
            return;
        }

        _context.Post(_ => handler(value), null);
    }
}
