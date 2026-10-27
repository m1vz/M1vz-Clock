using M1vClock.App.Services;
using M1vClock.Benchmarks.Gpu;
using M1vClock.Benchmarks.Safety;
using M1vClock.Benchmarks.Stress;
using M1vClock.Tuning.Stability;

namespace M1vClock.App.Pages.Performance;

public sealed record StabilityProgress(TimeSpan Elapsed, TimeSpan Planned, float? CpuMaximum, float? GpuMaximum);

public enum StabilityStartStatus
{
    Started,
    OtherTestRunning,
    NoTemperatureSensor,
    NotEnoughMemory,
    Refused,
}

public sealed class StabilityStartException(StabilityStartStatus status) : InvalidOperationException(status.ToString())
{
    public StabilityStartStatus Status { get; } = status;
}

public interface IStabilityRunner
{
    Task<StabilityEvidence> RunAsync(StabilityTarget target, TimeSpan duration, IProgress<StabilityProgress> progress, CancellationToken cancellationToken);
}

public sealed record StabilityRunnerServices(
    StressSession Stress,
    HeavyTestGate Gate,
    IStressWorkloadFactory Factory,
    IMachineProfile Machine,
    AppSettingsService Settings,
    IHardwareErrorSource Errors,
    TimeProvider Time);

public sealed class StabilityRunner(StabilityRunnerServices services) : IStabilityRunner
{
    public static readonly TimeSpan ErrorPoll = TimeSpan.FromSeconds(5);

    public static StabilityStop StopOf(StressEnd end, bool stoppedForErrors) => end switch
    {
        StressEnd.Completed => StabilityStop.Completed,
        StressEnd.StoppedByUser => stoppedForErrors ? StabilityStop.HardwareErrors : StabilityStop.StoppedByUser,
        StressEnd.Temperature => StabilityStop.Temperature,
        StressEnd.MemoryErrors => StabilityStop.MemoryErrors,
        StressEnd.SensorLost => StabilityStop.SensorLost,
        StressEnd.WindowUnresponsive => StabilityStop.WindowUnresponsive,
        _ => StabilityStop.WorkloadFailed,
    };

    public async Task<StabilityEvidence> RunAsync(StabilityTarget target, TimeSpan duration, IProgress<StabilityProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        using var lease = await services.Gate.EnterAsync("stability", cancellationToken).ConfigureAwait(true)
            ?? throw new StabilityStartException(StabilityStartStatus.OtherTestRunning);
        var cpu = target is StabilityTarget.CpuAndMemory or StabilityTarget.Everything;
        var gpu = target is StabilityTarget.Gpu or StabilityTarget.Everything && services.Factory.GpuAvailable;
        IStressWorkload? cpuRam = cpu ? services.Factory.CreateCpuRam(true, true, 0.6) : null;
        IStressWorkload? burn = null;
        try
        {
            if (gpu)
            {
                var machine = await services.Machine.GetAsync(cancellationToken).ConfigureAwait(true);
                burn = services.Factory.CreateGpu(GpuBurnSettings.High(1920, 1080, false, 4, machine.Gpu));
            }

            var settings = services.Settings.Current;
            var limits = new SafetyLimits(
                Math.Clamp(settings.StressCpuLimit, SafetyLimits.CpuMinimum, SafetyLimits.CpuHardMaximum),
                Math.Clamp(settings.StressGpuLimit, SafetyLimits.GpuMinimum, SafetyLimits.GpuHardMaximum),
                burn is not null ? SafetyLimits.GpuInterval : SafetyLimits.CpuInterval);
            var start = services.Time.GetUtcNow();
            float? cpuMax = null;
            float? gpuMax = null;
            void OnProgress(object? sender, StressProgress update)
            {
                cpuMax = Max(cpuMax, update.Sample?.Cpu?.Temperature);
                gpuMax = Max(gpuMax, update.Sample?.Gpu?.Temperature);
                progress.Report(new StabilityProgress(update.Elapsed, update.Duration, cpuMax, gpuMax));
            }

            services.Stress.Progress += OnProgress;
            try
            {
                var started = services.Stress.Start(new StressPlan(cpu, cpu, burn is not null, duration, limits), cpuRam, burn);
                if (started.Status != StressStartStatus.Started)
                {
                    throw new StabilityStartException(started.Status switch
                    {
                        StressStartStatus.NoTemperatureSensor => StabilityStartStatus.NoTemperatureSensor,
                        StressStartStatus.NotEnoughMemory => StabilityStartStatus.NotEnoughMemory,
                        StressStartStatus.AlreadyRunning => StabilityStartStatus.OtherTestRunning,
                        _ => StabilityStartStatus.Refused,
                    });
                }

                var stoppedForErrors = false;
                using var stop = cancellationToken.Register(services.Stress.Stop);
                var completion = services.Stress.Completion!;
                while (!completion.IsCompleted)
                {
                    await Task.WhenAny(completion, Task.Delay(ErrorPoll, services.Time, CancellationToken.None)).ConfigureAwait(true);
                    if (!completion.IsCompleted && (await Task.Run(() => services.Errors.ErrorsSince(start), CancellationToken.None).ConfigureAwait(true)).Total > 0)
                    {
                        stoppedForErrors = true;
                        services.Stress.Stop();
                    }
                }

                var outcome = await completion.ConfigureAwait(true);
                var errors = await Task.Run(() => services.Errors.ErrorsSince(start), CancellationToken.None).ConfigureAwait(true);
                return new StabilityEvidence(
                    target,
                    StopOf(outcome.End, stoppedForErrors),
                    outcome.Elapsed,
                    duration,
                    outcome.CpuRam.MemoryErrors,
                    Max(cpuMax, outcome.CpuMaximum),
                    Max(gpuMax, outcome.GpuMaximum),
                    errors);
            }
            finally
            {
                services.Stress.Progress -= OnProgress;
            }
        }
        finally
        {
            cpuRam?.Dispose();
            burn?.Dispose();
        }
    }

    private static float? Max(float? a, float? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
