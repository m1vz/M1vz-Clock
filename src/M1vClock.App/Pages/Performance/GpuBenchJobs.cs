using System.Globalization;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Benchmarks.Gpu;
using M1vClock.Benchmarks.Results;
using M1vClock.Benchmarks.Safety;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Performance;

public sealed class GpuSceneJob : IBenchJob
{
    private readonly IMachineProfile _machine;
    private readonly BenchOptionViewModel _resolution;
    private readonly BenchOptionViewModel _duration;
    private volatile GpuSceneBenchmark? _benchmark;
    private bool _hasGpu = true;

    public GpuSceneJob(IMachineProfile machine)
    {
        ArgumentNullException.ThrowIfNull(machine);
        _machine = machine;
        _resolution = new BenchOptionViewModel(
            "resolution",
            "Bench.Option.Resolution",
            [
                new BenchChoice("1920x1080", "1080p"),
                new BenchChoice("2560x1440", "1440p"),
                new BenchChoice("3840x2160", "4K"),
            ]);
        _duration = new BenchOptionViewModel(
            "duration",
            "Bench.Option.Duration",
            [
                new BenchChoice("30", "30 s"),
                new BenchChoice("60", "60 s"),
            ],
            1);
        Options = [_resolution, _duration];
        Refresh();
    }

    public string Id => "gpu";

    public string IconKey => "Icon.Gpu";

    public string Title => Loc.Get("Bench.Gpu.Title");

    public string Description => Loc.Get("Bench.Gpu.Description");

    public IReadOnlyList<BenchOptionViewModel> Options { get; }

    public IReadOnlyList<string> HistoryTests { get; } = [BenchmarkTests.GpuScene];

    public ThermalComponent? Thermal => ThermalComponent.Gpu;

    public IPreviewSource? Preview => null;

    public string? UnavailableReason => _hasGpu ? null : Loc.Get("Bench.Gpu.None");

    public void Refresh()
    {
        _hasGpu = GpuSupport.HasHardwareAdapter();
    }

    public BenchPoll Poll()
    {
        var benchmark = _benchmark;
        if (benchmark is null)
        {
            return BenchPoll.Idle;
        }

        var fps = benchmark.CurrentFps;
        return new BenchPoll("Bench.Phase.Gpu", benchmark.Progress, fps > 0 ? Loc.Format("Bench.Gpu.Live", fps.ToString("0", Loc.Culture)) : string.Empty);
    }

    public async Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var parts = _resolution.Value.Split('x');
        var width = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var height = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var seconds = int.Parse(_duration.Value, CultureInfo.InvariantCulture);
        var machine = await _machine.GetAsync(cancellationToken).ConfigureAwait(true);
        var benchmark = new GpuSceneBenchmark();
        _benchmark = benchmark;
        var options = new GpuSceneOptions(width, height, TimeSpan.FromSeconds(seconds), machine.Gpu);
        GpuSceneResult result;
        try
        {
            result = await Task.Factory.StartNew(
                () => benchmark.Run(options, cancellationToken),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).ConfigureAwait(true);
        }
        finally
        {
            _benchmark = null;
        }

        var draft = new BenchJobDraft(BenchmarkTests.GpuScene, 1, result.Score, BenchmarkCatalog.MetricsOf(result));
        var rows = new List<InfoRowViewModel>
        {
            new(Loc.Get("Bench.Gpu.Result.Fps"), Loc.Format("Bench.Result.Fps", result.AverageFps.ToString("0.0", Loc.Culture))),
            new(Loc.Get("Bench.Gpu.Result.Low"), Loc.Format("Bench.Result.Fps", result.LowFps.ToString("0.0", Loc.Culture))),
            new(Loc.Get("Bench.Gpu.Result.Frames"), result.Frames.ToString("N0", Loc.Culture)),
            new(Loc.Get("Bench.Gpu.Result.Resolution"), $"{result.Width} × {result.Height}"),
            new(Loc.Get("Bench.Gpu.Result.Adapter"), result.Adapter),
        };
        return new BenchJobOutcome([draft], rows, [Loc.Get("Bench.Gpu.Result.Note")]);
    }
}

public interface IAvailabilityNotifier
{
    event EventHandler? AvailabilityChanged;
}

public sealed class RayTracingJob : IBenchJob, IAvailabilityNotifier
{
    private readonly IMachineProfile _machine;
    private readonly ILogger _logger;
    private string? _reason;
    private int _checking;

    public RayTracingJob(IMachineProfile machine, ILogger<RayTracingJob> logger)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(logger);
        _machine = machine;
        _logger = logger;
        _reason = Loc.Get("Bench.Rt.Checking");
    }

    public event EventHandler? AvailabilityChanged;

    public string Id => "raytracing";

    public string IconKey => "Icon.Gpu";

    public string Title => Loc.Get("Bench.Rt.Title");

    public string Description => Loc.Get("Bench.Rt.Description");

    public IReadOnlyList<BenchOptionViewModel> Options { get; } = [];

    public IReadOnlyList<string> HistoryTests { get; } = [];

    public ThermalComponent? Thermal => ThermalComponent.Gpu;

    public IPreviewSource? Preview => null;

    public string? UnavailableReason => Volatile.Read(ref _reason);

    public void Refresh()
    {
        if (Interlocked.Exchange(ref _checking, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _reason, Loc.Get("Bench.Rt.Checking"));
        _ = CheckAsync();
    }

    public BenchPoll Poll() => BenchPoll.Idle;

    public Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("O teste de ray tracing ainda não está disponível.");

    private async Task CheckAsync()
    {
        string reason;
        try
        {
            var machine = await _machine.GetAsync(CancellationToken.None).ConfigureAwait(false);
            var support = await Task.Run(() => RayTracingCheck.Check(machine.Gpu, _logger)).ConfigureAwait(false);
            reason = support.Status switch
            {
                RayTracingStatus.Supported => Loc.Get("Bench.Rt.NotReady"),
                RayTracingStatus.NoHardwareRayTracing => Loc.Format("Bench.Rt.Unsupported", support.Adapter),
                RayTracingStatus.Direct3D12Unavailable => Loc.Get("Bench.Rt.NoDirect3D12"),
                _ => Loc.Get("Bench.Gpu.None"),
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException or InsufficientMemoryException)
        {
            AppLog.RayTracingCheckFailed(_logger, ex);
            reason = Loc.Get("Bench.Rt.NoDirect3D12");
        }

        Volatile.Write(ref _reason, reason);
        Interlocked.Exchange(ref _checking, 0);
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
