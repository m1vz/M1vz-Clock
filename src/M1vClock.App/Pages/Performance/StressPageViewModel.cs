using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Dialogs;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Benchmarks.Gpu;
using M1vClock.Benchmarks.Io;
using M1vClock.Benchmarks.Safety;
using M1vClock.Benchmarks.Stress;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Performance;

public interface IStressWorkloadFactory
{
    bool GpuAvailable { get; }

    IStressWorkload CreateCpuRam(bool cpu, bool ram, double ramFraction);

    IStressWorkload? CreateGpu(GpuBurnSettings settings);
}

public sealed class StressWorkloadFactory(ILoggerFactory loggers) : IStressWorkloadFactory
{
    private readonly Lazy<bool> _gpu = new(GpuSupport.HasHardwareAdapter);

    public bool GpuAvailable => _gpu.Value;

    public IStressWorkload CreateCpuRam(bool cpu, bool ram, double ramFraction) =>
        new StressRunner(cpu, ram, ramFraction, loggers.CreateLogger<StressRunner>());

    public IStressWorkload? CreateGpu(GpuBurnSettings settings) => new GpuBurn(settings, loggers.CreateLogger<GpuBurn>());
}

public sealed partial class StressPageViewModel : PageViewModel, IDisposable
{
    public const int LogLimit = 50;
    public const double LimitStep = 5;

    private static readonly double[] RamFractions = [0.4, 0.6, 0.8];
    private static readonly int[] DurationMinutes = [0, 5, 15, 30, 60];

    private readonly StressSession _session;
    private readonly HeavyTestGate _gate;
    private readonly IStressWorkloadFactory _factory;
    private readonly IMachineProfile _machine;
    private readonly DialogService _dialogs;
    private readonly AppSettingsService _settings;
    private readonly IUiThread _ui;
    private readonly TimeProvider _clock;
    private readonly ILogger<StressPageViewModel> _logger;
    private bool _disposed;
    private double _lastChartTime = double.NaN;

    public StressPageViewModel(
        StressSession session,
        HeavyTestGate gate,
        IStressWorkloadFactory factory,
        IMachineProfile machine,
        DialogService dialogs,
        AppSettingsService settings,
        IUiThread ui,
        TimeProvider clock,
        ILogger<StressPageViewModel> logger)
        : base(PageCatalog.Find("stress"))
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _session = session;
        _gate = gate;
        _factory = factory;
        _machine = machine;
        _dialogs = dialogs;
        _settings = settings;
        _ui = ui;
        _clock = clock;
        _logger = logger;
        CpuSeries = new TimeSeries(Loc.Get("Hw.Overview.Legend.Cpu"), TimeSpan.FromMinutes(10));
        GpuSeries = new TimeSeries(Loc.Get("Hw.Overview.Legend.Gpu"), TimeSpan.FromMinutes(10));
        TemperatureCard = new OverviewCardViewModel(Loc.Get("Stress.Live.CpuTemperature"));
        LoadCard = new OverviewCardViewModel(Loc.Get("Stress.Live.CpuLoad"));
        PowerCard = new OverviewCardViewModel(Loc.Get("Stress.Live.CpuPower"));
        MemoryCard = new OverviewCardViewModel(Loc.Get("Stress.Live.Memory"));
        GpuTemperatureCard = new OverviewCardViewModel(Loc.Get("Stress.Live.GpuTemperature"));
        GpuFpsCard = new OverviewCardViewModel(Loc.Get("Stress.Live.GpuFps"));
        ElapsedCard = new OverviewCardViewModel(Loc.Get("Stress.Live.Elapsed"));
        ResetCards();
        RebuildCards();
        StatusText = Loc.Get("Stress.Status.Idle");
        _gate.Changed += OnGateChanged;
    }

    public TimeSeries CpuSeries { get; }

    public TimeSeries GpuSeries { get; }

    public OverviewCardViewModel TemperatureCard { get; }

    public OverviewCardViewModel LoadCard { get; }

    public OverviewCardViewModel PowerCard { get; }

    public OverviewCardViewModel MemoryCard { get; }

    public OverviewCardViewModel GpuTemperatureCard { get; }

    public OverviewCardViewModel GpuFpsCard { get; }

    public OverviewCardViewModel ElapsedCard { get; }

    public ObservableCollection<string> Log { get; } = [];

    public ObservableCollection<OverviewCardViewModel> Cards { get; } = [];

    public bool GpuAvailable => _factory.GpuAvailable;

    public string ButtonText => Loc.Get(IsRunning ? "Stress.Stop" : "Stress.Start");

    public bool IsIdle => !IsRunning;

    public bool CanToggle => IsRunning || (!_gate.IsBusy && HasSelection);

    public bool HasSelection => CpuEnabled || RamEnabled || GpuEnabled;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool MessageIsWarning => HasMessage && MessageTone is MetricState.Warning or MetricState.Neutral;

    public bool MessageIsCritical => HasMessage && MessageTone == MetricState.Critical;

    public bool OtherTestRunning => _gate.IsBusy && !IsRunning;

    public string OtherTestText => Loc.Format("Bench.OtherRunning", _gate.CurrentName ?? string.Empty);

    public bool HasLog => Log.Count > 0;

    public bool HasSummary => Summary is not null;

    public string ChartUnit => _settings.Current.TemperatureUnit == TemperatureUnit.Fahrenheit ? "°F" : "°C";

    public double CpuLimitDisplay => ToUnit(CpuLimit);

    public string CpuLimitText => FormatTemperature(CpuLimit);

    public string GpuLimitText => FormatTemperature(GpuLimit);

    public bool CanLowerCpuLimit => IsIdle && CpuLimit - LimitStep >= SafetyLimits.CpuMinimum;

    public bool CanRaiseCpuLimit => IsIdle && CpuLimit + LimitStep <= SafetyLimits.CpuHardMaximum;

    public bool CanLowerGpuLimit => IsIdle && GpuLimit - LimitStep >= SafetyLimits.GpuMinimum;

    public bool CanRaiseGpuLimit => IsIdle && GpuLimit + LimitStep <= SafetyLimits.GpuHardMaximum;

    public string RamPlanText
    {
        get
        {
            var planned = StressRunner.PlannedRamBytes(PhysicalMemory.AvailableBytes(), RamFractions[Math.Clamp(RamFractionIndex, 0, RamFractions.Length - 1)]);
            return planned < StressRunner.ChunkBytes ? Loc.Get("Stress.Ram.TooLittle") : Loc.Format("Stress.Ram.Plan", Units.Bytes(planned));
        }
    }

    [ObservableProperty]
    public partial bool CpuEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool RamEnabled { get; set; }

    [ObservableProperty]
    public partial bool GpuEnabled { get; set; }

    [ObservableProperty]
    public partial int RamFractionIndex { get; set; } = 1;

    [ObservableProperty]
    public partial int DurationIndex { get; set; } = 2;

    [ObservableProperty]
    public partial int GpuResolutionIndex { get; set; } = 1;

    [ObservableProperty]
    public partial int GpuMsaaIndex { get; set; } = 1;

    [ObservableProperty]
    public partial int GpuIntensityIndex { get; set; } = 1;

    [ObservableProperty]
    public partial double CpuLimit { get; set; } = SafetyLimits.CpuDefault;

    [ObservableProperty]
    public partial double GpuLimit { get; set; } = SafetyLimits.GpuDefault;

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState StatusTone { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MetricState MessageTone { get; set; }

    [ObservableProperty]
    public partial InfoSectionViewModel? Summary { get; set; }

    public override void OnNavigatedTo()
    {
        _gate.StopRequested += OnStopRequested;
        OnPropertyChanged(nameof(RamPlanText));
    }

    public override void OnNavigatedFrom()
    {
        _gate.StopRequested -= OnStopRequested;
        if (IsRunning)
        {
            _session.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Changed -= OnGateChanged;
        _gate.StopRequested -= OnStopRequested;
        if (IsRunning)
        {
            _session.Stop();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ToggleAsync()
    {
        if (IsRunning)
        {
            _session.Stop();
            return;
        }

        await StartAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RaiseCpuLimitAsync()
    {
        var next = CpuLimit + LimitStep;
        if (next > SafetyLimits.CpuHardMaximum)
        {
            return;
        }

        if (SafetyLimits.CpuNeedsConfirmation(next) && !await ConfirmHighLimitAsync(ThermalComponent.Cpu, next).ConfigureAwait(true))
        {
            return;
        }

        CpuLimit = next;
    }

    [RelayCommand]
    public void LowerCpuLimit() => CpuLimit = Math.Max(SafetyLimits.CpuMinimum, CpuLimit - LimitStep);

    [RelayCommand]
    public async Task RaiseGpuLimitAsync()
    {
        var next = GpuLimit + LimitStep;
        if (next > SafetyLimits.GpuHardMaximum)
        {
            return;
        }

        if (SafetyLimits.GpuNeedsConfirmation(next) && !await ConfirmHighLimitAsync(ThermalComponent.Gpu, next).ConfigureAwait(true))
        {
            return;
        }

        GpuLimit = next;
    }

    [RelayCommand]
    public void LowerGpuLimit() => GpuLimit = Math.Max(SafetyLimits.GpuMinimum, GpuLimit - LimitStep);

    [RelayCommand]
    public void ClearLog()
    {
        Log.Clear();
        OnPropertyChanged(nameof(HasLog));
    }

    partial void OnCpuEnabledChanged(bool value) => OnSelectionChanged();

    partial void OnRamEnabledChanged(bool value) => OnSelectionChanged();

    partial void OnGpuEnabledChanged(bool value) => OnSelectionChanged();

    partial void OnRamFractionIndexChanged(int value) => OnPropertyChanged(nameof(RamPlanText));

    partial void OnCpuLimitChanged(double value)
    {
        OnPropertyChanged(nameof(CpuLimitText));
        OnPropertyChanged(nameof(CpuLimitDisplay));
        OnPropertyChanged(nameof(CanLowerCpuLimit));
        OnPropertyChanged(nameof(CanRaiseCpuLimit));
    }

    partial void OnGpuLimitChanged(double value)
    {
        OnPropertyChanged(nameof(GpuLimitText));
        OnPropertyChanged(nameof(CanLowerGpuLimit));
        OnPropertyChanged(nameof(CanRaiseGpuLimit));
    }

    partial void OnIsRunningChanged(bool value)
    {
        foreach (var name in new[]
        {
            nameof(ButtonText), nameof(IsIdle), nameof(CanToggle), nameof(OtherTestRunning), nameof(CanLowerCpuLimit), nameof(CanRaiseCpuLimit),
            nameof(CanLowerGpuLimit), nameof(CanRaiseGpuLimit),
        })
        {
            OnPropertyChanged(name);
        }
    }

    partial void OnMessageChanged(string value) => NotifyMessage();

    partial void OnMessageToneChanged(MetricState value) => NotifyMessage();

    partial void OnSummaryChanged(InfoSectionViewModel? value) => OnPropertyChanged(nameof(HasSummary));

    private void NotifyMessage()
    {
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(MessageIsWarning));
        OnPropertyChanged(nameof(MessageIsCritical));
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanToggle));
        RebuildCards();
    }

    private void OnGateChanged(object? sender, EventArgs e)
    {
        _ui.Post(() =>
        {
            OnPropertyChanged(nameof(CanToggle));
            OnPropertyChanged(nameof(OtherTestRunning));
            OnPropertyChanged(nameof(OtherTestText));
        });
    }

    private void OnStopRequested(object? sender, EventArgs e)
    {
        if (IsRunning)
        {
            _session.Stop();
        }
    }

    private async Task StartAsync()
    {
        SetMessage(string.Empty, MetricState.Neutral);
        if (!HasSelection)
        {
            SetMessage(Loc.Get("Stress.Error.NothingSelected"), MetricState.Warning);
            return;
        }

        var gpu = GpuEnabled && _factory.GpuAvailable;
        var minutes = DurationMinutes[Math.Clamp(DurationIndex, 0, DurationMinutes.Length - 1)];
        if (!await ConfirmStartAsync(gpu, minutes).ConfigureAwait(true))
        {
            return;
        }

        using var lease = await _gate.EnterAsync("stress", CancellationToken.None).ConfigureAwait(true);
        if (lease is null)
        {
            SetMessage(Loc.Format("Bench.OtherRunning", _gate.CurrentName ?? string.Empty), MetricState.Warning);
            return;
        }

        var fraction = RamFractions[Math.Clamp(RamFractionIndex, 0, RamFractions.Length - 1)];
        IStressWorkload? cpuRam = CpuEnabled || RamEnabled ? _factory.CreateCpuRam(CpuEnabled, RamEnabled, fraction) : null;
        IStressWorkload? gpuWorkload = null;
        if (gpu)
        {
            var machine = await _machine.GetAsync(CancellationToken.None).ConfigureAwait(true);
            gpuWorkload = _factory.CreateGpu(GpuSettings(machine.Gpu));
        }

        try
        {
            var plan = new StressPlan(
                CpuEnabled,
                RamEnabled,
                gpuWorkload is not null,
                TimeSpan.FromMinutes(minutes),
                new SafetyLimits(CpuLimit, GpuLimit, gpuWorkload is not null ? SafetyLimits.GpuInterval : SafetyLimits.CpuInterval));
            PrepareForRun();
            _session.Progress += OnProgress;
            var started = _session.Start(plan, cpuRam, gpuWorkload);
            if (started.Status != StressStartStatus.Started)
            {
                _session.Progress -= OnProgress;
                SetMessage(StartRefusal(started), MetricState.Critical);
                StatusText = Loc.Get("Stress.Status.Idle");
                StatusTone = MetricState.Neutral;
                return;
            }

            IsRunning = true;
            StatusText = Loc.Get("Stress.Status.Running");
            StatusTone = MetricState.Good;
            AddLog(StartLogLine(plan, fraction));
            var outcome = await _session.Completion!.ConfigureAwait(true);
            _session.Progress -= OnProgress;
            Present(outcome);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException or InsufficientMemoryException)
        {
            AppLog.StressFailed(_logger, ex);
            _session.Progress -= OnProgress;
            SetMessage(Loc.Get("Stress.Error.Generic"), MetricState.Critical);
            StatusText = Loc.Get("Stress.Status.Aborted");
            StatusTone = MetricState.Critical;
        }
        finally
        {
            cpuRam?.Dispose();
            gpuWorkload?.Dispose();
            IsRunning = false;
        }
    }

    private GpuBurnSettings GpuSettings(string adapterName)
    {
        var (width, height, fullscreen) = Math.Clamp(GpuResolutionIndex, 0, 3) switch
        {
            0 => (1280, 720, false),
            1 => (1920, 1080, false),
            2 => (2560, 1440, false),
            _ => (1920, 1080, true),
        };
        var msaa = Math.Clamp(GpuMsaaIndex, 0, 2) switch
        {
            0 => 1,
            1 => 4,
            _ => 8,
        };
        return Math.Clamp(GpuIntensityIndex, 0, 2) switch
        {
            0 => GpuBurnSettings.Normal(width, height, fullscreen, msaa, adapterName),
            1 => GpuBurnSettings.High(width, height, fullscreen, msaa, adapterName),
            _ => GpuBurnSettings.Extreme(width, height, fullscreen, msaa, adapterName),
        };
    }

    private async Task<bool> ConfirmStartAsync(bool gpu, int minutes)
    {
        var changes = new List<string>();
        if (CpuEnabled)
        {
            changes.Add(Loc.Format("Stress.Confirm.Cpu", CpuLimitText));
        }

        if (RamEnabled)
        {
            changes.Add(Loc.Format("Stress.Confirm.Ram", RamPlanText));
        }

        if (gpu)
        {
            changes.Add(Loc.Format("Stress.Confirm.Gpu", GpuLimitText));
        }

        changes.Add(minutes == 0 ? Loc.Get("Stress.Confirm.Unlimited") : Loc.Format("Stress.Confirm.Duration", minutes));
        var request = new DialogRequest(
            Loc.Get("Stress.Confirm.Title"),
            Loc.Get("Stress.Confirm.Message"),
            changes,
            Loc.Get("Stress.Confirm.Undo"),
            Loc.Get("Stress.Confirm.Start"),
            Loc.Get("Common.Cancel"),
            DialogTone.Neutral);
        return await _dialogs.ShowAsync(request).ConfigureAwait(true);
    }

    private async Task<bool> ConfirmHighLimitAsync(ThermalComponent component, double celsius)
    {
        var name = Loc.Get(component == ThermalComponent.Cpu ? "Bench.Component.Cpu" : "Bench.Component.Gpu");
        var first = new DialogRequest(
            Loc.Format("Stress.HighLimit.Title", name),
            Loc.Format("Stress.HighLimit.Message", FormatTemperature(celsius), name),
            [Loc.Format("Stress.HighLimit.Change", FormatTemperature(celsius))],
            Loc.Get("Stress.HighLimit.Undo"),
            Loc.Get("Stress.HighLimit.Continue"),
            Loc.Get("Common.Cancel"),
            DialogTone.Destructive);
        if (!await _dialogs.ShowAsync(first).ConfigureAwait(true))
        {
            return false;
        }

        var second = new DialogRequest(
            Loc.Get("Stress.HighLimit.SecondTitle"),
            Loc.Format("Stress.HighLimit.SecondMessage", FormatTemperature(celsius), name),
            [],
            null,
            Loc.Get("Stress.HighLimit.Confirm"),
            Loc.Get("Common.Cancel"),
            DialogTone.Destructive);
        return await _dialogs.ShowAsync(second).ConfigureAwait(true);
    }

    private void OnProgress(object? sender, StressProgress progress) => _ui.Post(() => ShowProgress(progress));

    private void ShowProgress(StressProgress progress)
    {
        var unit = _settings.Current.TemperatureUnit;
        ElapsedCard.Set(FormatSpan(progress.Elapsed), string.Empty, RemainingText(progress), MetricState.Neutral, string.Empty);
        if (progress.Sample is { } sample)
        {
            ShowCpu(sample.Cpu, unit);
            ShowGpu(sample.Gpu, progress.Gpu, unit);
            AddChartPoint(sample, unit);
        }

        if (progress.CpuRam.MemoryBytes > 0 || progress.CpuRam.MemoryPasses > 0)
        {
            MemoryCard.Set(
                $"{progress.CpuRam.MemoryPasses}",
                string.Empty,
                Loc.Format("Stress.Live.MemoryDetail", progress.CpuRam.MemoryErrors, Units.Bytes(progress.CpuRam.MemoryBytes)),
                progress.CpuRam.MemoryErrors > 0 ? MetricState.Critical : MetricState.Good,
                string.Empty);
        }
    }

    private void ShowCpu(ComponentSample? cpu, TemperatureUnit unit)
    {
        var temperature = SensorFormat.Value(SensorKind.Temperature, cpu?.Temperature, unit).Split(' ');
        TemperatureCard.Set(
            temperature[0],
            temperature.Length > 1 ? temperature[1] : string.Empty,
            Loc.Format("Stress.Live.Limit", CpuLimitText),
            cpu?.Temperature >= CpuLimit - LimitStep ? MetricState.Warning : MetricState.Neutral,
            string.Empty);
        Set(LoadCard, SensorKind.Load, cpu?.Load);
        Set(PowerCard, SensorKind.Power, cpu?.Power);
    }

    private void ShowGpu(ComponentSample? gpu, WorkloadStatus status, TemperatureUnit unit)
    {
        if (!GpuEnabled)
        {
            return;
        }

        var temperature = SensorFormat.Value(SensorKind.Temperature, gpu?.Temperature, unit).Split(' ');
        GpuTemperatureCard.Set(temperature[0], temperature.Length > 1 ? temperature[1] : string.Empty, Loc.Format("Stress.Live.Limit", GpuLimitText), MetricState.Neutral, string.Empty);
        GpuFpsCard.Set(status.Fps.ToString("0", Loc.Culture), "FPS", Loc.Format("Stress.Live.FpsAverage", status.AverageFps.ToString("0", Loc.Culture)), MetricState.Neutral, string.Empty);
    }

    private void AddChartPoint(SafetySample sample, TemperatureUnit unit)
    {
        var time = sample.At.TotalSeconds;
        if (!double.IsNaN(_lastChartTime) && time <= _lastChartTime)
        {
            return;
        }

        _lastChartTime = time;
        if (sample.Cpu?.Temperature is { } cpu && CpuEnabled)
        {
            CpuSeries.Add(unit == TemperatureUnit.Fahrenheit ? cpu * 9 / 5 + 32 : cpu);
        }

        if (sample.Gpu?.Temperature is { } gpu && GpuEnabled)
        {
            GpuSeries.Add(unit == TemperatureUnit.Fahrenheit ? gpu * 9 / 5 + 32 : gpu);
        }
    }

    private static void Set(OverviewCardViewModel card, SensorKind kind, float? value)
    {
        var text = SensorFormat.Value(kind, value).Split(' ');
        card.Set(text[0], text.Length > 1 ? text[1] : string.Empty, string.Empty, MetricState.Neutral, string.Empty);
    }

    private static string RemainingText(StressProgress progress)
    {
        if (progress.Duration <= TimeSpan.Zero)
        {
            return Loc.Get("Stress.Live.NoLimit");
        }

        var remaining = progress.Duration - progress.Elapsed;
        return remaining <= TimeSpan.Zero ? string.Empty : Loc.Format("Bench.Remaining", FormatSpan(remaining));
    }

    private static string FormatSpan(TimeSpan span) =>
        span.ToString(span.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss", Loc.Culture);

    private void PrepareForRun()
    {
        Summary = null;
        _lastChartTime = double.NaN;
        CpuSeries.Clear();
        GpuSeries.Clear();
        ResetCards();
        SetMessage(string.Empty, MetricState.Neutral);
    }

    private void RebuildCards()
    {
        Cards.Clear();
        if (CpuEnabled)
        {
            Cards.Add(TemperatureCard);
            Cards.Add(LoadCard);
            Cards.Add(PowerCard);
        }

        if (RamEnabled)
        {
            Cards.Add(MemoryCard);
        }

        if (GpuEnabled)
        {
            Cards.Add(GpuTemperatureCard);
            Cards.Add(GpuFpsCard);
        }

        Cards.Add(ElapsedCard);
    }

    private void ResetCards()
    {
        foreach (var card in new[] { TemperatureCard, LoadCard, PowerCard, MemoryCard, GpuTemperatureCard, GpuFpsCard, ElapsedCard })
        {
            card.Set(Units.Missing, string.Empty, string.Empty, MetricState.Neutral, string.Empty);
        }
    }

    private void Present(StressOutcome outcome)
    {
        var (text, tone) = EndText(outcome);
        StatusText = text;
        StatusTone = tone;
        var section = new InfoSectionViewModel(Loc.Get("Stress.Summary.Title"), "Icon.Stress");
        var rows = new List<InfoRowViewModel>
        {
            new(Loc.Get("Stress.Summary.Duration"), FormatSpan(outcome.Elapsed)),
        };
        if (outcome.CpuMaximum is { } cpu)
        {
            rows.Add(new InfoRowViewModel(Loc.Get("Stress.Summary.CpuMax"), FormatTemperature(cpu)));
        }

        if (outcome.GpuMaximum is { } gpu)
        {
            rows.Add(new InfoRowViewModel(Loc.Get("Stress.Summary.GpuMax"), FormatTemperature(gpu)));
        }

        if (RamEnabled)
        {
            rows.Add(new InfoRowViewModel(Loc.Get("Stress.Summary.RamPasses"), outcome.CpuRam.MemoryPasses.ToString("N0", Loc.Culture)));
            rows.Add(new InfoRowViewModel(Loc.Get("Stress.Summary.RamErrors"), outcome.CpuRam.MemoryErrors.ToString("N0", Loc.Culture)));
        }

        if (GpuEnabled && outcome.Gpu.Frames > 0)
        {
            rows.Add(new InfoRowViewModel(Loc.Get("Stress.Summary.GpuFrames"), outcome.Gpu.Frames.ToString("N0", Loc.Culture)));
            rows.Add(new InfoRowViewModel(Loc.Get("Stress.Summary.GpuFps"), outcome.Gpu.AverageFps.ToString("0", Loc.Culture)));
        }

        section.StatusText = text;
        section.StatusTone = tone;
        section.SetReady(rows);
        Summary = section;
        AddLog(Loc.Format("Stress.Log.Ended", text, FormatSpan(outcome.Elapsed)));
    }

    private (string Text, MetricState Tone) EndText(StressOutcome outcome) => outcome.End switch
    {
        StressEnd.Completed => (Loc.Get("Stress.Status.Completed"), MetricState.Good),
        StressEnd.StoppedByUser => (Loc.Get("Stress.Status.Stopped"), MetricState.Neutral),
        StressEnd.Temperature => (
            Loc.Format(
                "Stress.Status.Temperature",
                Loc.Get(outcome.Trip?.Component == ThermalComponent.Gpu ? "Bench.Component.Gpu" : "Bench.Component.Cpu"),
                FormatTemperature(outcome.Trip?.Value ?? 0),
                FormatTemperature(outcome.Trip?.Limit ?? 0)),
            MetricState.Critical),
        StressEnd.MemoryErrors => (Loc.Format("Stress.Status.MemoryErrors", outcome.CpuRam.MemoryErrors.ToString("N0", Loc.Culture)), MetricState.Critical),
        StressEnd.SensorLost => (Loc.Get("Stress.Status.SensorLost"), MetricState.Critical),
        StressEnd.WindowUnresponsive => (Loc.Get("Stress.Status.Unresponsive"), MetricState.Critical),
        _ => (Loc.Format("Stress.Status.Failed", outcome.Gpu.Error ?? string.Empty), MetricState.Critical),
    };

    private static string StartRefusal(StressStartResult result) => result.Status switch
    {
        StressStartStatus.NoTemperatureSensor => Loc.Format(
            "Stress.Error.NoSensor",
            Loc.Get(result.MissingSensor == ThermalComponent.Gpu ? "Bench.Component.Gpu" : "Bench.Component.Cpu"),
            Loc.Get("Hw.Sensor.Problem.PawnIoCommand")),
        StressStartStatus.AlreadyRunning => Loc.Get("Stress.Error.AlreadyRunning"),
        StressStartStatus.NothingSelected => Loc.Get("Stress.Error.NothingSelected"),
        _ => Loc.Get("Stress.Error.Generic"),
    };

    private string StartLogLine(StressPlan plan, double fraction)
    {
        var cpu = plan.Cpu ? Loc.Format("Stress.Log.On", Loc.Format("Stress.Log.Limit", CpuLimitText)) : Loc.Get("Stress.Log.Off");
        var ram = plan.Ram ? Loc.Format("Stress.Log.On", Loc.Format("Stress.Log.Fraction", fraction.ToString("P0", Loc.Culture))) : Loc.Get("Stress.Log.Off");
        var gpu = plan.Gpu ? Loc.Format("Stress.Log.On", Loc.Format("Stress.Log.Limit", GpuLimitText)) : Loc.Get("Stress.Log.Off");
        var duration = plan.Duration == TimeSpan.Zero ? Loc.Get("Stress.Log.NoLimit") : Loc.Format("Stress.Log.Minutes", plan.Duration.TotalMinutes.ToString("0.##", Loc.Culture));
        return Loc.Format("Stress.Log.Started", cpu, ram, gpu, duration);
    }

    private void AddLog(string line)
    {
        Log.Insert(0, $"{_clock.GetLocalNow().ToString("T", Loc.Culture)}  {line}");
        while (Log.Count > LogLimit)
        {
            Log.RemoveAt(Log.Count - 1);
        }

        OnPropertyChanged(nameof(HasLog));
    }

    private void SetMessage(string text, MetricState tone)
    {
        Message = text;
        MessageTone = tone;
    }

    private double ToUnit(double celsius) =>
        _settings.Current.TemperatureUnit == TemperatureUnit.Fahrenheit ? celsius * 9 / 5 + 32 : celsius;

    private string FormatTemperature(double celsius) =>
        SensorFormat.Value(SensorKind.Temperature, (float)celsius, _settings.Current.TemperatureUnit);
}
