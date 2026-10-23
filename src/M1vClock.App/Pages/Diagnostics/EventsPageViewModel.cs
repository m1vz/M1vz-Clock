using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Diagnostics.Events;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Diagnostics;

public sealed partial class EventsPageViewModel : HardwarePageViewModel
{
    public static readonly IReadOnlyList<TimeSpan> Windows = [TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(14), TimeSpan.FromDays(30)];

    private readonly IEventService _events;
    private EventReport? _report;

    public EventsPageViewModel(IEventService events, ILogger<EventsPageViewModel> logger)
        : base(PageCatalog.Find("events"), logger)
    {
        ArgumentNullException.ThrowIfNull(events);
        _events = events;
    }

    public ObservableCollection<EventGroupViewModel> Items { get; } = [];

    public IReadOnlyList<string> WindowLabels { get; } =
    [
        Loc.Get("Diag.Events.Window.Day"),
        Loc.Get("Diag.Events.Window.Week"),
        Loc.Get("Diag.Events.Window.Fortnight"),
        Loc.Get("Diag.Events.Window.Month"),
    ];

    [ObservableProperty]
    public partial int WindowIndex { get; set; } = 1;

    [ObservableProperty]
    public partial bool ShowNoise { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string CriticalText { get; set; } = "—";

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "—";

    [ObservableProperty]
    public partial string WarningText { get; set; } = "—";

    [ObservableProperty]
    public partial string KindsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HiddenText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problems { get; set; } = string.Empty;

    public bool HasHidden => HiddenText.Length > 0;

    public bool HasProblems => Problems.Length > 0;

    public bool IsEmpty => !IsLoading && Items.Count == 0 && !HasProblems && string.IsNullOrEmpty(PageError);

    public MetricState CriticalState => _report is { } report && report.CountOf(EventSeverity.Critical) > 0 ? MetricState.Critical : MetricState.Neutral;

    public MetricState ErrorState => _report is { } report && report.CountOf(EventSeverity.Error) > 0 ? MetricState.Warning : MetricState.Neutral;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            _report = await _events.ReadAsync(Windows[Math.Clamp(WindowIndex, 0, Windows.Count - 1)], cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            Show(_report);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    partial void OnWindowIndexChanged(int value) => _ = RefreshAsync();

    partial void OnShowNoiseChanged(bool value)
    {
        if (_report is { } report)
        {
            Show(report);
        }
    }

    partial void OnHiddenTextChanged(string value) => OnPropertyChanged(nameof(HasHidden));

    partial void OnProblemsChanged(string value) => OnPropertyChanged(nameof(HasProblems));

    private void Show(EventReport report)
    {
        CriticalText = report.CountOf(EventSeverity.Critical).ToString("N0", Loc.Culture);
        ErrorText = report.CountOf(EventSeverity.Error).ToString("N0", Loc.Culture);
        WarningText = report.CountOf(EventSeverity.Warning).ToString("N0", Loc.Culture);
        KindsText = Loc.Format("Diag.Events.Summary.Kinds", report.Groups.Count.ToString("N0", Loc.Culture));
        OnPropertyChanged(nameof(CriticalState));
        OnPropertyChanged(nameof(ErrorState));
        var quiet = report.Groups.Where(g => !IsNoise(g)).ToList();
        var shown = ShowNoise ? report.Groups : quiet;
        Items.Clear();
        foreach (var group in shown)
        {
            Items.Add(new EventGroupViewModel(group));
        }

        var hidden = report.Groups.Where(IsNoise).ToList();
        HiddenText = ShowNoise || hidden.Count == 0
            ? string.Empty
            : Loc.Format("Diag.Events.Hidden", hidden.Sum(g => g.Count).ToString("N0", Loc.Culture), hidden.Count.ToString(Loc.Culture));
        Problems = ProblemsOf(report);
        OnPropertyChanged(nameof(IsEmpty));
    }

    public static bool IsNoise(EventGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.IsNoisy || group.Known is { Kind: KnownEventKind.Noise };
    }

    private static string ProblemsOf(EventReport report)
    {
        var lines = report.FailedLogs
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => Loc.Format("Diag.Events.LogFailed", f.Key, f.Value))
            .ToList();
        if (report.Truncated)
        {
            lines.Add(Loc.Get("Diag.Events.Truncated"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
