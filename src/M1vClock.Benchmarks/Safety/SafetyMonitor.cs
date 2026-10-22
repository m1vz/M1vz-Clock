using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks.Safety;

public sealed class SafetyMonitor : IAsyncDisposable
{
    private readonly IComponentProbe _probe;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<ThermalComponent, int> _lost = [];
    private readonly Dictionary<ThermalComponent, float> _maximum = [];
    private CancellationTokenSource? _cancellation;
    private Task _loop = Task.CompletedTask;
    private SafetyTrip? _trip;
    private SafetySample? _latest;
    private int _unresponsive;
    private long _startedAt;

    public SafetyMonitor(IComponentProbe probe, TimeProvider clock, ILogger<SafetyMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _probe = probe;
        _clock = clock;
        _logger = logger;
    }

    public event EventHandler<SafetyTrip>? Tripped;

    public event EventHandler<SafetySample>? SampleReady;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _cancellation is not null;
            }
        }
    }

    public SafetyTrip? Trip
    {
        get
        {
            lock (_gate)
            {
                return _trip;
            }
        }
    }

    public SafetySample? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public float? MaximumOf(ThermalComponent component)
    {
        lock (_gate)
        {
            return _maximum.TryGetValue(component, out var value) ? value : null;
        }
    }

    public bool CanRead(ThermalComponent component) => _probe.Read(component)?.Temperature is > 0;

    public void Start(
        IReadOnlyCollection<ThermalComponent> watched,
        SafetyLimits limits,
        Func<long>? memoryErrors = null,
        Func<bool>? isResponsive = null)
    {
        ArgumentNullException.ThrowIfNull(watched);
        ArgumentNullException.ThrowIfNull(limits);
        var components = new HashSet<ThermalComponent>(watched);
        lock (_gate)
        {
            if (_cancellation is not null)
            {
                throw new InvalidOperationException("O monitor de segurança já está rodando.");
            }

            _trip = null;
            _latest = null;
            _unresponsive = 0;
            _lost.Clear();
            _maximum.Clear();
            _startedAt = _clock.GetTimestamp();
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _loop = Task.Run(() => RunAsync(components, limits, memoryErrors, isResponsive, token), CancellationToken.None);
        }
    }

    public void RequestStop(TripReason reason = TripReason.UserRequested)
    {
        RaiseTrip(new SafetyTrip(reason, null, 0, 0, Elapsed()));
    }

    public void ReportFailure(ThermalComponent component)
    {
        RaiseTrip(new SafetyTrip(TripReason.ComponentFailed, component, 0, 0, Elapsed()));
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task loop;
        lock (_gate)
        {
            cancellation = _cancellation;
            _cancellation = null;
            loop = _loop;
        }

        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync().ConfigureAwait(false);
        await loop.ConfigureAwait(false);
        cancellation.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(
        HashSet<ThermalComponent> watched,
        SafetyLimits limits,
        Func<long>? memoryErrors,
        Func<bool>? isResponsive,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(limits.Interval, _clock);
        try
        {
            Evaluate(watched, limits, memoryErrors, isResponsive);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Evaluate(watched, limits, memoryErrors, isResponsive);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private void Evaluate(HashSet<ThermalComponent> watched, SafetyLimits limits, Func<long>? memoryErrors, Func<bool>? isResponsive)
    {
        var at = Elapsed();
        ComponentSample? cpu = null;
        ComponentSample? gpu = null;
        foreach (var component in watched)
        {
            ComponentSample? sample;
            try
            {
                sample = _probe.Read(component);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                SafetyLog.ProbeFailed(_logger, ex, component);
                sample = null;
            }

            if (component == ThermalComponent.Cpu)
            {
                cpu = sample;
            }
            else
            {
                gpu = sample;
            }

            Judge(component, sample, limits, at);
        }

        var snapshot = new SafetySample(at, cpu, gpu);
        lock (_gate)
        {
            _latest = snapshot;
        }

        SampleReady?.Invoke(this, snapshot);

        if (memoryErrors is not null)
        {
            var errors = memoryErrors();
            if (errors > 0)
            {
                RaiseTrip(new SafetyTrip(TripReason.MemoryErrors, null, errors, 0, at));
            }
        }

        if (isResponsive is not null)
        {
            if (isResponsive())
            {
                _unresponsive = 0;
            }
            else if (++_unresponsive >= SafetyLimits.UnresponsiveSamples)
            {
                RaiseTrip(new SafetyTrip(TripReason.WindowUnresponsive, null, _unresponsive, SafetyLimits.UnresponsiveSamples, at));
            }
        }
    }

    private void Judge(ThermalComponent component, ComponentSample? sample, SafetyLimits limits, TimeSpan at)
    {
        var temperature = sample?.Temperature;
        if (temperature is not > 0 || !float.IsFinite(temperature.Value))
        {
            var lost = _lost.GetValueOrDefault(component) + 1;
            _lost[component] = lost;
            if (lost >= SafetyLimits.SensorLostSamples)
            {
                RaiseTrip(new SafetyTrip(TripReason.SensorLost, component, lost, SafetyLimits.SensorLostSamples, at));
            }

            return;
        }

        _lost[component] = 0;
        lock (_gate)
        {
            if (!_maximum.TryGetValue(component, out var maximum) || temperature.Value > maximum)
            {
                _maximum[component] = temperature.Value;
            }
        }

        var limit = limits.LimitOf(component);
        if (temperature.Value >= limit)
        {
            RaiseTrip(new SafetyTrip(TripReason.Temperature, component, temperature.Value, limit, at));
        }
    }

    private void RaiseTrip(SafetyTrip trip)
    {
        lock (_gate)
        {
            if (_trip is not null)
            {
                return;
            }

            _trip = trip;
        }

        SafetyLog.Tripped(_logger, trip.Reason, trip.Component?.ToString() ?? "-", trip.Value, trip.Limit);
        Tripped?.Invoke(this, trip);
    }

    private TimeSpan Elapsed() => _clock.GetElapsedTime(_startedAt);
}
