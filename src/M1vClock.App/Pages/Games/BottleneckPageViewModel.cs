using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Services;
using M1vClock.Games.Bottleneck;
using M1vClock.Hardware.Sensors;

namespace M1vClock.App.Pages.Games;

public sealed record LimiterRowView(string Name, string PercentText, double Fraction);

public sealed partial class BottleneckPageViewModel : PageViewModel, IDisposable
{
    public const int MinimumSamples = 10;

    private static readonly int[] DurationMinutes = [1, 3, 5, 10];

    private readonly ISensorService _sensors;
    private readonly IUiThread _ui;
    private readonly AppSettingsService _settings;
    private readonly TimeProvider _clock;
    private IDisposable? _lease;
    private BottleneckSession? _session;

    public BottleneckPageViewModel(ISensorService sensors, IUiThread ui, AppSettingsService settings, TimeProvider clock)
        : base(PageCatalog.Find("bottleneck"))
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        _sensors = sensors;
        _ui = ui;
        _settings = settings;
        _clock = clock;
        DurationLabels = DurationMinutes.Select(m => Loc.Get($"Game.Bottleneck.Duration.{m}")).ToList();
        DurationIndex = 1;
    }

    public IReadOnlyList<string> DurationLabels { get; }

    public ObservableCollection<LimiterRowView> ReportRows { get; } = [];

    public bool HasLive => !IsWaiting;

    public bool CanStart => !IsRecording && !IsWaiting;

    public bool HasNoSensors => _sensors.State is SensorServiceState.Paused or SensorServiceState.Failed or SensorServiceState.Stopped && !IsRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLive))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial bool IsWaiting { get; set; } = true;

    [ObservableProperty]
    public partial int DurationIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    public partial double Fraction { get; set; }

    [ObservableProperty]
    public partial string RecordingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LiveTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LiveAdvice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState LiveTone { get; set; } = MetricState.Neutral;

    [ObservableProperty]
    public partial string GpuText { get; set; } = Units.Missing;

    [ObservableProperty]
    public partial string CpuText { get; set; } = Units.Missing;

    [ObservableProperty]
    public partial string CoreText { get; set; } = Units.Missing;

    [ObservableProperty]
    public partial string VramText { get; set; } = Units.Missing;

    [ObservableProperty]
    public partial string RamText { get; set; } = Units.Missing;

    [ObservableProperty]
    public partial string GpuTempText { get; set; } = Units.Missing;

    [ObservableProperty]
    public partial string CpuTempText { get; set; } = Units.Missing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    public partial string ReportMain { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReportAdvice { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReportSamples { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReportAverages { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ReportPeaks { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    [NotifyPropertyChangedFor(nameof(HasReportWarning))]
    public partial string ReportWarning { get; set; } = string.Empty;

    public bool HasReport => ReportMain.Length > 0 || ReportWarning.Length > 0;

    public bool HasReportWarning => ReportWarning.Length > 0;

    public override void OnNavigatedTo()
    {
        _sensors.SnapshotReady += OnSnapshot;
        _sensors.StateChanged += OnStateChanged;
        _lease ??= _sensors.Subscribe();
        OnPropertyChanged(nameof(HasNoSensors));
        if (_sensors.Latest is { } latest)
        {
            Apply(latest);
        }
    }

    public override void OnNavigatedFrom()
    {
        Detach();
        _session = null;
        IsRecording = false;
        OnPropertyChanged(nameof(HasNoSensors));
    }

    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    public void Start()
    {
        if (!CanStart)
        {
            return;
        }

        var minutes = DurationMinutes[Math.Clamp(DurationIndex, 0, DurationMinutes.Length - 1)];
        _session = new BottleneckSession(_clock.GetUtcNow(), TimeSpan.FromMinutes(minutes));
        ClearReport();
        Fraction = 0;
        RecordingText = Loc.Format("Game.Bottleneck.Recording", Clock(TimeSpan.Zero), Clock(_session.Duration));
        IsRecording = true;
        OnPropertyChanged(nameof(HasNoSensors));
    }

    [RelayCommand]
    public void Stop() => Finish();

    private void Detach()
    {
        _sensors.SnapshotReady -= OnSnapshot;
        _sensors.StateChanged -= OnStateChanged;
        _lease?.Dispose();
        _lease = null;
    }

    private void OnSnapshot(object? sender, SensorSnapshot snapshot) => _ui.Post(() => Apply(snapshot));

    private void OnStateChanged(object? sender, EventArgs e) => _ui.Post(() => OnPropertyChanged(nameof(HasNoSensors)));

    private static string Clock(TimeSpan time) => time.ToString(@"m\:ss", Loc.Culture);

    private void Apply(SensorSnapshot snapshot)
    {
        var sample = BottleneckSampler.From(snapshot);
        var unit = _settings.Current.TemperatureUnit;
        var limiter = sample.Classify();
        IsWaiting = false;
        LiveTitle = Loc.Get($"Game.Bottleneck.Limiter.{limiter}");
        LiveAdvice = Loc.Get($"Game.Bottleneck.Advice.{limiter}");
        LiveTone = ToneOf(limiter);
        GpuText = SensorFormat.Value(SensorKind.Load, sample.GpuLoad, unit, Loc.Culture);
        CpuText = SensorFormat.Value(SensorKind.Load, sample.CpuTotal, unit, Loc.Culture);
        CoreText = SensorFormat.Value(SensorKind.Load, sample.CpuBusiestCore, unit, Loc.Culture);
        VramText = SensorFormat.Value(SensorKind.Load, (float?)sample.VramPercent, unit, Loc.Culture);
        RamText = SensorFormat.Value(SensorKind.Load, sample.RamPercent, unit, Loc.Culture);
        GpuTempText = SensorFormat.Value(SensorKind.Temperature, sample.GpuTemperature, unit, Loc.Culture);
        CpuTempText = SensorFormat.Value(SensorKind.Temperature, sample.CpuTemperature, unit, Loc.Culture);
        OnPropertyChanged(nameof(HasNoSensors));
        if (_session is not { } session)
        {
            return;
        }

        session.Add(sample);
        var now = _clock.GetUtcNow();
        Fraction = session.Progress(now);
        RecordingText = Loc.Format("Game.Bottleneck.Recording", Clock(now - session.Start < TimeSpan.Zero ? TimeSpan.Zero : now - session.Start), Clock(session.Duration));
        if (session.IsDone(now))
        {
            Finish();
        }
    }

    private void Finish()
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        IsRecording = false;
        var report = session.Report();
        OnPropertyChanged(nameof(HasNoSensors));
        ClearReport();
        if (report.Main is not { } main)
        {
            ReportWarning = Loc.Get("Game.Bottleneck.Report.None");
            return;
        }

        var unit = _settings.Current.TemperatureUnit;
        ReportMain = Loc.Format("Game.Bottleneck.Report.Main", Loc.Get($"Game.Bottleneck.Limiter.{main}"));
        ReportAdvice = Loc.Get($"Game.Bottleneck.Advice.{main}");
        ReportSamples = Loc.Format("Game.Bottleneck.Report.Samples", report.Samples.ToString("N0", Loc.Culture));
        ReportAverages = Loc.Format(
            "Game.Bottleneck.Report.Averages",
            SensorFormat.Value(SensorKind.Load, (float?)report.AverageGpuLoad, unit, Loc.Culture),
            SensorFormat.Value(SensorKind.Load, (float?)report.AverageCpuTotal, unit, Loc.Culture));
        ReportPeaks = Loc.Format(
            "Game.Bottleneck.Report.Peaks",
            SensorFormat.Value(SensorKind.Load, (float?)report.PeakCpuCore, unit, Loc.Culture),
            SensorFormat.Value(SensorKind.Load, (float?)report.PeakVram, unit, Loc.Culture),
            SensorFormat.Value(SensorKind.Load, (float?)report.PeakRam, unit, Loc.Culture),
            SensorFormat.Value(SensorKind.Temperature, (float?)report.PeakGpuTemperature, unit, Loc.Culture),
            SensorFormat.Value(SensorKind.Temperature, (float?)report.PeakCpuTemperature, unit, Loc.Culture));
        foreach (var share in report.Distribution)
        {
            ReportRows.Add(new LimiterRowView(Loc.Get($"Game.Bottleneck.Limiter.{share.Limiter}"), share.Percent.ToString("0", Loc.Culture) + " %", share.Percent / 100.0));
        }

        if (report.Samples < MinimumSamples)
        {
            ReportWarning = Loc.Format("Game.Bottleneck.Report.TooShort", report.Samples.ToString("N0", Loc.Culture));
        }
    }

    private void ClearReport()
    {
        ReportRows.Clear();
        ReportMain = ReportAdvice = ReportSamples = ReportAverages = ReportPeaks = ReportWarning = string.Empty;
    }

    private static MetricState ToneOf(Limiter limiter) => limiter switch
    {
        Limiter.Gpu => MetricState.Good,
        Limiter.Cpu => MetricState.Warning,
        Limiter.Vram or Limiter.Ram or Limiter.Thermal => MetricState.Critical,
        _ => MetricState.Neutral,
    };
}
