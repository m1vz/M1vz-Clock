using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Diagnostics.Crashes;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Diagnostics;

public sealed partial class CrashesPageViewModel : HardwarePageViewModel
{
    public static readonly IReadOnlyList<TimeSpan> Windows = [TimeSpan.FromDays(7), TimeSpan.FromDays(30), TimeSpan.FromDays(90)];

    private static readonly string[] PartOrder =
    [
        CrashService.SettingsPart,
        CrashService.ShutdownsPart,
        CrashService.AppCrashesPart,
        CrashService.KernelDumpsPart,
        CrashService.UserDumpsPart,
    ];

    private readonly ICrashService _crashes;

    public CrashesPageViewModel(ICrashService crashes, ILogger<CrashesPageViewModel> logger)
        : base(PageCatalog.Find("crashes"), logger)
    {
        ArgumentNullException.ThrowIfNull(crashes);
        _crashes = crashes;
        Settings = new InfoSectionViewModel(Loc.Get("Diag.Crashes.Settings.Title"), "Icon.Crashes");
    }

    public InfoSectionViewModel Settings { get; }

    public ObservableCollection<DiagnosticRowViewModel> Shutdowns { get; } = [];

    public ObservableCollection<DiagnosticRowViewModel> AppCrashes { get; } = [];

    public ObservableCollection<DiagnosticRowViewModel> KernelDumps { get; } = [];

    public ObservableCollection<DiagnosticRowViewModel> UserDumps { get; } = [];

    public IReadOnlyList<string> WindowLabels { get; } =
    [
        Loc.Get("Diag.Events.Window.Week"),
        Loc.Get("Diag.Events.Window.Month"),
        Loc.Get("Diag.Crashes.Window.Quarter"),
    ];

    [ObservableProperty]
    public partial int WindowIndex { get; set; } = 1;

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string BlueScreensText { get; set; } = "—";

    [ObservableProperty]
    public partial string ShutdownsText { get; set; } = "—";

    [ObservableProperty]
    public partial string AppCrashesText { get; set; } = "—";

    [ObservableProperty]
    public partial string AppKindsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState BlueScreensState { get; set; }

    [ObservableProperty]
    public partial MetricState ShutdownsState { get; set; }

    [ObservableProperty]
    public partial string Advice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problems { get; set; } = string.Empty;

    public bool HasAdvice => Advice.Length > 0;

    public bool HasProblems => Problems.Length > 0;

    public bool ShowShutdownsEmpty => !IsLoading && Shutdowns.Count == 0;

    public bool ShowAppsEmpty => !IsLoading && AppCrashes.Count == 0;

    public bool ShowKernelDumpsEmpty => !IsLoading && KernelDumps.Count == 0;

    public bool ShowUserDumpsEmpty => !IsLoading && UserDumps.Count == 0;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        Settings.SetLoading();
        try
        {
            var report = await _crashes.ReadAsync(Windows[Math.Clamp(WindowIndex, 0, Windows.Count - 1)], cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            Show(report);
        }
        finally
        {
            IsLoading = false;
            NotifyEmpty();
        }
    }

    partial void OnWindowIndexChanged(int value) => _ = RefreshAsync();

    partial void OnAdviceChanged(string value) => OnPropertyChanged(nameof(HasAdvice));

    partial void OnProblemsChanged(string value) => OnPropertyChanged(nameof(HasProblems));

    private static void Fill(ObservableCollection<DiagnosticRowViewModel> target, IEnumerable<DiagnosticRowViewModel> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    private static string ProblemsOf(CrashReport report) =>
        string.Join(
            Environment.NewLine,
            PartOrder.Where(report.Failures.ContainsKey).Select(part => Loc.Format("Diag.Crashes.Failed", Loc.Get($"Diag.Crashes.Part.{part}"), report.Failures[part])));

    private void Show(CrashReport report)
    {
        ShowSettings(report);
        Fill(Shutdowns, report.Shutdowns.Select(CrashRows.Shutdown));
        Fill(AppCrashes, report.AppCrashes.Select(CrashRows.App));
        Fill(KernelDumps, report.KernelDumps.Select(CrashRows.Dump));
        Fill(UserDumps, report.UserDumps.Select(CrashRows.Dump));
        BlueScreensText = report.BlueScreens.ToString("N0", Loc.Culture);
        ShutdownsText = report.Shutdowns.Count.ToString("N0", Loc.Culture);
        AppCrashesText = report.AppCrashCount.ToString("N0", Loc.Culture);
        AppKindsText = Loc.Format("Diag.Crashes.Summary.AppKinds", report.AppCrashes.Select(c => c.App.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count().ToString(Loc.Culture));
        BlueScreensState = report.BlueScreens > 0 ? MetricState.Critical : MetricState.Neutral;
        ShutdownsState = report.Shutdowns.Count > 0 ? MetricState.Warning : MetricState.Neutral;
        Problems = ProblemsOf(report);
        NotifyEmpty();
    }

    private void ShowSettings(CrashReport report)
    {
        if (!report.Settings.TryGet(out var settings))
        {
            Settings.SetFailed(report.Settings.Error ?? Loc.Get("Hw.Common.Unknown"));
            Advice = string.Empty;
            return;
        }

        Settings.StatusText = Loc.Get(settings.Collects ? "Diag.Crashes.Settings.Status.On" : "Diag.Crashes.Settings.Status.Off");
        Settings.StatusTone = settings.Collects ? MetricState.Good : MetricState.Warning;
        Settings.SetReady(CrashRows.Settings(settings));
        Advice = CrashRows.Advice(settings);
    }

    private void NotifyEmpty()
    {
        OnPropertyChanged(nameof(ShowShutdownsEmpty));
        OnPropertyChanged(nameof(ShowAppsEmpty));
        OnPropertyChanged(nameof(ShowKernelDumpsEmpty));
        OnPropertyChanged(nameof(ShowUserDumpsEmpty));
    }
}
