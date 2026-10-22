using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Services;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Health;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed partial class OverviewPageViewModel : PageViewModel, IDisposable
{
    public static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(60);

    private const int ActivityRowsShown = 5;
    private const int OneMinute = 60;
    private const int FiveMinutes = 300;
    private const int FifteenMinutes = 900;

    private readonly ISensorService _sensors;
    private readonly IHealthService _health;
    private readonly OverviewHistory _history;
    private readonly ActivityLog _activity;
    private readonly AppSettingsService _settings;
    private readonly AppInfo _info;
    private readonly IUiThread _ui;
    private readonly TimeProvider _clock;
    private readonly ILogger<OverviewPageViewModel> _logger;
    private IDisposable? _lease;
    private ITimer? _timer;
    private CancellationTokenSource? _healthCancellation;
    private bool _refreshing;
    private bool _healthSawSensors;

    public OverviewPageViewModel(
        ISensorService sensors,
        IHealthService health,
        OverviewHistory history,
        ActivityLog activity,
        AppSettingsService settings,
        AppInfo info,
        IUiThread ui,
        TimeProvider clock,
        ILogger<OverviewPageViewModel> logger)
        : base(PageCatalog.Find("overview"))
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _sensors = sensors;
        _health = health;
        _history = history;
        _activity = activity;
        _settings = settings;
        _info = info;
        _ui = ui;
        _clock = clock;
        _logger = logger;
        Cpu = new OverviewCardViewModel(Loc.Get("Hw.Overview.Cpu"));
        Gpu = new OverviewCardViewModel(Loc.Get("Hw.Overview.Gpu"));
        Memory = new OverviewCardViewModel(Loc.Get("Hw.Overview.Memory"));
        Disk = new OverviewCardViewModel(Loc.Get("Hw.Overview.Disk"));
    }

    public OverviewCardViewModel Cpu { get; }

    public OverviewCardViewModel Gpu { get; }

    public OverviewCardViewModel Memory { get; }

    public OverviewCardViewModel Disk { get; }

    public ObservableCollection<HealthRowViewModel> HealthRows { get; } = [];

    public ObservableCollection<InfoRowViewModel> ActivityRows { get; } = [];

    public TimeSeries Primary => MetricIndex switch
    {
        1 => _history.CpuLoad,
        2 => _history.CpuPower,
        _ => _history.CpuTemperature,
    };

    public TimeSeries Secondary => MetricIndex switch
    {
        1 => _history.GpuLoad,
        2 => _history.GpuPower,
        _ => _history.GpuTemperature,
    };

    public string ChartUnit => MetricIndex switch
    {
        1 => "%",
        2 => "W",
        _ => _history.Unit == TemperatureUnit.Fahrenheit ? "°F" : "°C",
    };

    public double ChartFloor => MetricIndex == 0 ? double.NaN : 0;

    public double ChartMinimumRange => MetricIndex switch
    {
        1 => 25,
        2 => 20,
        _ => 10,
    };

    public double ChartWindowSeconds => WindowIndex switch
    {
        1 => FiveMinutes,
        2 => FifteenMinutes,
        _ => OneMinute,
    };

    public bool HasActivity => ActivityRows.Count > 0;

    public bool HasHealthFailure => !string.IsNullOrEmpty(HealthFailure);

    [ObservableProperty]
    public partial int MetricIndex { get; set; }

    [ObservableProperty]
    public partial int WindowIndex { get; set; }

    [ObservableProperty]
    public partial string HealthSummary { get; set; } = Loc.Get("Hw.Overview.Health.Loading");

    [ObservableProperty]
    public partial MetricState HealthTone { get; set; }

    [ObservableProperty]
    public partial bool IsHealthLoading { get; set; } = true;

    [ObservableProperty]
    public partial string HealthFailure { get; set; } = string.Empty;

    public override void OnNavigatedTo()
    {
        _sensors.SnapshotReady += OnSnapshot;
        _activity.Changed += OnActivityChanged;
        _lease ??= _sensors.Subscribe();
        if (_sensors.Latest is { } latest)
        {
            Apply(latest);
        }

        ShowActivity();
        _timer ??= _clock.CreateTimer(_ => _ui.Post(() => _ = RefreshHealthAsync()), null, HealthInterval, HealthInterval);
        _ = RefreshHealthAsync();
    }

    public override void OnNavigatedFrom() => Detach();

    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }

    public async Task RefreshHealthAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        var cancellation = new CancellationTokenSource();
        _healthCancellation = cancellation;
        try
        {
            var snapshot = _sensors.Latest;
            _healthSawSensors = snapshot is not null;
            var report = await _health.ReadAsync(snapshot, _info.IsElevated, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            ShowHealth(report);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.PageLoadFailed(_logger, ex, "overview");
            HealthFailure = Loc.Get("Hw.Overview.Health.Failed");
            IsHealthLoading = false;
        }
        finally
        {
            _refreshing = false;
            if (ReferenceEquals(_healthCancellation, cancellation))
            {
                _healthCancellation = null;
            }

            cancellation.Dispose();
        }

        if (!_healthSawSensors && _sensors.Latest is not null && _timer is not null)
        {
            await RefreshHealthAsync().ConfigureAwait(true);
        }
    }

    partial void OnMetricIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Primary));
        OnPropertyChanged(nameof(Secondary));
        OnPropertyChanged(nameof(ChartUnit));
        OnPropertyChanged(nameof(ChartFloor));
        OnPropertyChanged(nameof(ChartMinimumRange));
    }

    partial void OnWindowIndexChanged(int value) => OnPropertyChanged(nameof(ChartWindowSeconds));

    partial void OnHealthFailureChanged(string value) => OnPropertyChanged(nameof(HasHealthFailure));

    private void Detach()
    {
        _sensors.SnapshotReady -= OnSnapshot;
        _activity.Changed -= OnActivityChanged;
        _lease?.Dispose();
        _lease = null;
        _timer?.Dispose();
        _timer = null;
        _healthCancellation?.Cancel();
    }

    private void OnSnapshot(object? sender, SensorSnapshot snapshot) => _ui.Post(() => Apply(snapshot));

    private void OnActivityChanged(object? sender, EventArgs e) => ShowActivity();

    private void Apply(SensorSnapshot snapshot)
    {
        var unit = _settings.Current.TemperatureUnit;
        OverviewRows.Processor(Cpu, snapshot.CpuTemperature, snapshot.CpuLoad, snapshot.CpuPower, snapshot.CpuClock, unit, OverviewRows.CpuTemperatureState);
        OverviewRows.Processor(Gpu, snapshot.GpuTemperature, snapshot.GpuLoad, snapshot.GpuPower, snapshot.GpuClock, unit, OverviewRows.GpuTemperatureState);
        OverviewRows.Memory(Memory, snapshot);
        OnPropertyChanged(nameof(ChartUnit));
        if (!_healthSawSensors && !_refreshing && _timer is not null)
        {
            _ = RefreshHealthAsync();
        }
    }

    private void ShowHealth(HealthReport report)
    {
        HealthRows.Clear();
        foreach (var row in OverviewRows.Health(report))
        {
            HealthRows.Add(row);
        }

        HealthSummary = OverviewRows.HealthOverall(report);
        HealthTone = OverviewRows.HealthTone(report.Overall);
        HealthFailure = string.Empty;
        IsHealthLoading = false;
        OverviewRows.Disk(Disk, report.SystemVolume);
    }

    private void ShowActivity()
    {
        ActivityRows.Clear();
        foreach (var row in DeviceRows.Activity([.. _activity.Entries.Take(ActivityRowsShown)]))
        {
            ActivityRows.Add(row);
        }

        OnPropertyChanged(nameof(HasActivity));
    }
}
