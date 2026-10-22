using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.Diagnostics.Reliability;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Diagnostics;

public sealed partial class ReliabilityPageViewModel : HardwarePageViewModel
{
    public static readonly IReadOnlyList<TimeSpan> Windows = [TimeSpan.FromDays(7), TimeSpan.FromDays(30), TimeSpan.FromDays(90)];

    public static readonly IReadOnlyList<double> Guides = [1, 5, 8, 10];

    private readonly IReliabilityService _reliability;
    private ReliabilityReport? _report;

    public ReliabilityPageViewModel(IReliabilityService reliability, ILogger<ReliabilityPageViewModel> logger)
        : base(PageCatalog.Find("reliability"), logger)
    {
        ArgumentNullException.ThrowIfNull(reliability);
        _reliability = reliability;
    }

    public ObservableCollection<DiagnosticRowViewModel> Items { get; } = [];

    public IReadOnlyList<string> WindowLabels { get; } =
    [
        Loc.Get("Diag.Events.Window.Week"),
        Loc.Get("Diag.Events.Window.Month"),
        Loc.Get("Diag.Crashes.Window.Quarter"),
    ];

    [ObservableProperty]
    public partial int WindowIndex { get; set; } = 1;

    [ObservableProperty]
    public partial bool ShowChanges { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string CurrentText { get; set; } = "—";

    [ObservableProperty]
    public partial string LowestText { get; set; } = "—";

    [ObservableProperty]
    public partial string AverageText { get; set; } = "—";

    [ObservableProperty]
    public partial string VerdictText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState VerdictTone { get; set; }

    [ObservableProperty]
    public partial string FailuresText { get; set; } = "—";

    [ObservableProperty]
    public partial string ChangesText { get; set; } = "—";

    [ObservableProperty]
    public partial IReadOnlyList<ChartPoint>? ChartPoints { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<double>? Markers { get; set; }

    [ObservableProperty]
    public partial string ChartName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HiddenText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Problems { get; set; } = string.Empty;

    public bool HasChart => ChartPoints is { Count: > 1 };

    public bool HasHidden => HiddenText.Length > 0;

    public bool HasProblems => Problems.Length > 0;

    public bool IsEmpty => !IsLoading && Items.Count == 0;

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            _report = await _reliability.ReadAsync(Windows[Math.Clamp(WindowIndex, 0, Windows.Count - 1)], cancellationToken).ConfigureAwait(true);
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

    partial void OnShowChangesChanged(bool value)
    {
        if (_report is { } report)
        {
            ShowList(report);
        }
    }

    partial void OnChartPointsChanged(IReadOnlyList<ChartPoint>? value) => OnPropertyChanged(nameof(HasChart));

    partial void OnHiddenTextChanged(string value) => OnPropertyChanged(nameof(HasHidden));

    partial void OnProblemsChanged(string value) => OnPropertyChanged(nameof(HasProblems));

    private void Show(ReliabilityReport report)
    {
        CurrentText = ReliabilityRows.Index(report.Current);
        LowestText = ReliabilityRows.Index(report.Lowest);
        AverageText = ReliabilityRows.Index(report.Average);
        VerdictText = ReliabilityRows.Verdict(report.Verdict);
        VerdictTone = ReliabilityRows.Tone(report.Verdict);
        FailuresText = report.Records.IsSuccess ? report.Failures.ToString("N0", Loc.Culture) : "—";
        ChangesText = report.Records.IsSuccess
            ? (report.Count(ReliabilityKind.SoftwareInstalled) + report.Count(ReliabilityKind.SoftwareRemoved) + report.Count(ReliabilityKind.SoftwareReconfigured) + report.Count(ReliabilityKind.UpdateInstalled)).ToString("N0", Loc.Culture)
            : "—";
        ChartPoints = report.Stability.TryGet(out var points) ? ReliabilityRows.ChartPoints(points) : null;
        Markers = report.Records.TryGet(out var records) ? ReliabilityRows.FailureMarkers(records) : null;
        ChartName = ReliabilityRows.ChartName(report.Current);
        Problems = ProblemsOf(report);
        ShowList(report);
    }

    private void ShowList(ReliabilityReport report)
    {
        Items.Clear();
        var hidden = 0;
        if (report.Records.TryGet(out var records))
        {
            foreach (var record in records)
            {
                if (record.IsFailure || ShowChanges)
                {
                    Items.Add(ReliabilityRows.Record(record));
                }
                else
                {
                    hidden++;
                }
            }
        }

        HiddenText = hidden > 0 ? Loc.Format("Diag.Reliability.Hidden", hidden.ToString("N0", Loc.Culture)) : string.Empty;
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static string ProblemsOf(ReliabilityReport report)
    {
        var lines = new List<string>();
        if (!report.Stability.IsSuccess)
        {
            lines.Add(Loc.Format("Diag.Crashes.Failed", Loc.Get("Diag.Reliability.Part.stability"), report.Stability.Error ?? string.Empty));
        }

        if (!report.Records.IsSuccess)
        {
            lines.Add(Loc.Format("Diag.Crashes.Failed", Loc.Get("Diag.Reliability.Part.records"), report.Records.Error ?? string.Empty));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
