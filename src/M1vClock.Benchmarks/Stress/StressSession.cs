using M1vClock.Benchmarks.Safety;
using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks.Stress;

public enum StressStartStatus
{
    Started,
    NothingSelected,
    NoTemperatureSensor,
    AlreadyRunning,
    NotEnoughMemory,
}

public enum StressEnd
{
    Completed,
    StoppedByUser,
    Temperature,
    MemoryErrors,
    SensorLost,
    WindowUnresponsive,
    WorkloadFailed,
}

public sealed record StressPlan(bool Cpu, bool Ram, bool Gpu, TimeSpan Duration, SafetyLimits Limits);

public sealed record StressStartResult(StressStartStatus Status, ThermalComponent? MissingSensor);

public sealed record StressProgress(TimeSpan Elapsed, TimeSpan Duration, SafetySample? Sample, WorkloadStatus CpuRam, WorkloadStatus Gpu);

public sealed record StressOutcome(
    StressEnd End,
    SafetyTrip? Trip,
    TimeSpan Elapsed,
    float? CpuMaximum,
    float? GpuMaximum,
    WorkloadStatus CpuRam,
    WorkloadStatus Gpu);

public sealed class StressSession : IAsyncDisposable
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(250);

    private readonly SafetyMonitor _monitor;
    private readonly TimeProvider _clock;
    private readonly ILogger<StressSession> _logger;
    private readonly Lock _gate = new();
    private IStressWorkload? _cpuRam;
    private IStressWorkload? _gpu;
    private Task<StressOutcome>? _completion;

    public StressSession(SafetyMonitor monitor, TimeProvider clock, ILogger<StressSession> logger)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _monitor = monitor;
        _clock = clock;
        _logger = logger;
    }

    public event EventHandler<StressProgress>? Progress;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _completion is { IsCompleted: false };
            }
        }
    }

    public Task<StressOutcome>? Completion
    {
        get
        {
            lock (_gate)
            {
                return _completion;
            }
        }
    }

    public StressStartResult Start(StressPlan plan, IStressWorkload? cpuRam, IStressWorkload? gpu, Func<bool>? isResponsive = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_gate)
        {
            if (_completion is { IsCompleted: false })
            {
                return Refuse(StressStartStatus.AlreadyRunning, null);
            }
        }

        if (!plan.Cpu && !plan.Ram && !plan.Gpu)
        {
            return Refuse(StressStartStatus.NothingSelected, null);
        }

        if ((plan.Cpu || plan.Ram) && cpuRam is null)
        {
            return Refuse(StressStartStatus.NothingSelected, null);
        }

        if (plan.Gpu && gpu is null)
        {
            return Refuse(StressStartStatus.NothingSelected, null);
        }

        var watched = new List<ThermalComponent>();
        if (plan.Cpu)
        {
            watched.Add(ThermalComponent.Cpu);
        }

        if (plan.Gpu)
        {
            watched.Add(ThermalComponent.Gpu);
        }

        foreach (var component in watched)
        {
            if (!_monitor.CanRead(component))
            {
                return Refuse(StressStartStatus.NoTemperatureSensor, component);
            }
        }

        lock (_gate)
        {
            _cpuRam = plan.Cpu || plan.Ram ? cpuRam : null;
            _gpu = plan.Gpu ? gpu : null;
        }

        var runner = _cpuRam;
        var wake = new CancellationTokenSource();
        EventHandler<SafetyTrip> onTripped = (_, _) => wake.Cancel();
        _monitor.Tripped += onTripped;
        _monitor.Start(watched, plan.Limits, () => runner?.Status.MemoryErrors ?? 0, isResponsive);
        var started = _clock.GetTimestamp();
        SafetyLog.StressStarted(_logger, plan.Cpu, plan.Ram, plan.Gpu, plan.Duration.TotalMinutes);
        _cpuRam?.Start();
        _gpu?.Start();
        var completion = Task.Run(() => RunAsync(plan, started, wake, onTripped), CancellationToken.None);
        lock (_gate)
        {
            _completion = completion;
        }

        return new StressStartResult(StressStartStatus.Started, null);
    }

    public void Stop()
    {
        _monitor.RequestStop();
    }

    public async ValueTask DisposeAsync()
    {
        _monitor.RequestStop();
        Task<StressOutcome>? completion;
        lock (_gate)
        {
            completion = _completion;
        }

        if (completion is not null)
        {
            await completion.ConfigureAwait(false);
        }

        await _monitor.StopAsync().ConfigureAwait(false);
    }
    private async Task<StressOutcome> RunAsync(StressPlan plan, long started, CancellationTokenSource wake, EventHandler<SafetyTrip> onTripped)
    {
        using var timer = new PeriodicTimer(UpdateInterval, _clock);
        while (_monitor.Trip is null)
        {
            var ticked = true;
            try
            {
                ticked = await timer.WaitForNextTickAsync(wake.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ticked = false;
            }

            if (!ticked)
            {
                break;
            }

            var elapsed = _clock.GetElapsedTime(started);
            Progress?.Invoke(this, new StressProgress(elapsed, plan.Duration, _monitor.Latest, CpuRamStatus(), GpuStatus()));
            if (_monitor.Trip is not null)
            {
                break;
            }

            if (plan.Duration > TimeSpan.Zero && elapsed >= plan.Duration)
            {
                break;
            }

            if (_gpu is { Status.Error: not null })
            {
                _monitor.ReportFailure(ThermalComponent.Gpu);
                break;
            }

            if (_gpu is { Status.Alive: false })
            {
                _monitor.RequestStop();
                break;
            }
        }

        _monitor.Tripped -= onTripped;
        wake.Dispose();
        return await FinishAsync(started).ConfigureAwait(false);
    }

    private async Task<StressOutcome> FinishAsync(long started)
    {
        var cpuRam = CpuRamStatus();
        var gpu = GpuStatus();
        _cpuRam?.Halt();
        _gpu?.Halt();
        var trip = _monitor.Trip;
        var elapsed = _clock.GetElapsedTime(started);
        var cpuMax = _monitor.MaximumOf(ThermalComponent.Cpu);
        var gpuMax = _monitor.MaximumOf(ThermalComponent.Gpu);
        await _monitor.StopAsync().ConfigureAwait(false);
        var end = trip is null ? StressEnd.Completed : trip.Reason switch
        {
            TripReason.Temperature => StressEnd.Temperature,
            TripReason.MemoryErrors => StressEnd.MemoryErrors,
            TripReason.SensorLost => StressEnd.SensorLost,
            TripReason.WindowUnresponsive => StressEnd.WindowUnresponsive,
            TripReason.ComponentFailed => StressEnd.WorkloadFailed,
            _ => StressEnd.StoppedByUser,
        };
        SafetyLog.StressEnded(_logger, end, (int)elapsed.TotalSeconds);

        return new StressOutcome(end, trip, elapsed, cpuMax, gpuMax, cpuRam, gpu);
    }

    private WorkloadStatus CpuRamStatus() => _cpuRam?.Status ?? WorkloadStatus.Idle;

    private WorkloadStatus GpuStatus() => _gpu?.Status ?? WorkloadStatus.Idle;

    private StressStartResult Refuse(StressStartStatus status, ThermalComponent? missing)
    {
        SafetyLog.StressRefused(_logger, status);
        return new StressStartResult(status, missing);
    }
}
