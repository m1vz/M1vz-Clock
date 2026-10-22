using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Hosting;

namespace M1vClock.App.Services;

public sealed class OverviewHistory : IHostedService
{
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(15);

    private const double FahrenheitScale = 9.0 / 5.0;
    private const double FahrenheitOffset = 32;

    private readonly ISensorService _sensors;
    private readonly AppSettingsService _settings;
    private readonly IUiThread _ui;
    private IDisposable? _lease;
    private TemperatureUnit _unit;

    public OverviewHistory(ISensorService sensors, AppSettingsService settings, IUiThread ui)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ui);
        _sensors = sensors;
        _settings = settings;
        _ui = ui;
        _unit = settings.Current.TemperatureUnit;
        CpuTemperature = Series("Hw.Overview.Legend.Cpu");
        GpuTemperature = Series("Hw.Overview.Legend.Gpu");
        CpuLoad = Series("Hw.Overview.Legend.Cpu");
        GpuLoad = Series("Hw.Overview.Legend.Gpu");
        CpuPower = Series("Hw.Overview.Legend.Cpu");
        GpuPower = Series("Hw.Overview.Legend.Gpu");
    }

    public TimeSeries CpuTemperature { get; }

    public TimeSeries GpuTemperature { get; }

    public TimeSeries CpuLoad { get; }

    public TimeSeries GpuLoad { get; }

    public TimeSeries CpuPower { get; }

    public TimeSeries GpuPower { get; }

    public TemperatureUnit Unit => _unit;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sensors.SnapshotReady += OnSnapshot;
        _settings.Changed += OnSettingsChanged;
        _lease ??= _sensors.Subscribe();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sensors.SnapshotReady -= OnSnapshot;
        _settings.Changed -= OnSettingsChanged;
        _lease?.Dispose();
        _lease = null;
        return Task.CompletedTask;
    }

    public void Add(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Add(CpuTemperature, snapshot.CpuTemperature is { } cpu ? Temperature(cpu) : null);
        Add(GpuTemperature, snapshot.GpuTemperature is { } gpu ? Temperature(gpu) : null);
        Add(CpuLoad, snapshot.CpuLoad);
        Add(GpuLoad, snapshot.GpuLoad);
        Add(CpuPower, snapshot.CpuPower);
        Add(GpuPower, snapshot.GpuPower);
    }

    private static TimeSeries Series(string labelKey) => new(Loc.Get(labelKey), Retention);

    private static void Add(TimeSeries series, double? value)
    {
        if (value is { } number)
        {
            series.Add(number);
        }
    }

    private double Temperature(float celsius) => _unit == TemperatureUnit.Fahrenheit ? celsius * FahrenheitScale + FahrenheitOffset : celsius;

    private void OnSnapshot(object? sender, SensorSnapshot snapshot) => _ui.Post(() => Add(snapshot));

    private void OnSettingsChanged(object? sender, EventArgs e) => _ui.Post(() =>
    {
        var unit = _settings.Current.TemperatureUnit;
        if (unit == _unit)
        {
            return;
        }

        _unit = unit;
        CpuTemperature.Clear();
        GpuTemperature.Clear();
    });
}
