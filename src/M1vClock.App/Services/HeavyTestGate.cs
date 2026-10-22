using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Services;

public sealed class HeavyTestGate
{
    public static readonly TimeSpan SensorWait = TimeSpan.FromSeconds(25);

    private readonly ISensorService _sensors;
    private readonly TimeProvider _clock;
    private readonly ILogger<HeavyTestGate> _logger;
    private readonly Lock _gate = new();
    private bool _busy;
    private string? _name;

    public HeavyTestGate(ISensorService sensors, TimeProvider clock, ILogger<HeavyTestGate> logger)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _sensors = sensors;
        _clock = clock;
        _logger = logger;
    }

    public event EventHandler? Changed;

    public event EventHandler? StopRequested;

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _busy;
            }
        }
    }

    public string? CurrentName
    {
        get
        {
            lock (_gate)
            {
                return _name;
            }
        }
    }

    public async Task<HeavyTestLease?> EnterAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            if (_busy)
            {
                return null;
            }

            _busy = true;
            _name = name;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        IDisposable? subscription = null;
        try
        {
            subscription = _sensors.Subscribe();
            await WaitForSensorsAsync(cancellationToken).ConfigureAwait(false);
            var pause = _sensors.Pause();
            AppLog.HeavyTestEntered(_logger, name);
            return new HeavyTestLease(this, subscription, pause, name);
        }
        catch
        {
            subscription?.Dispose();
            Leave(name);
            throw;
        }
    }

    public void RequestStop() => StopRequested?.Invoke(this, EventArgs.Empty);

    internal void Leave(string name)
    {
        lock (_gate)
        {
            _busy = false;
            _name = null;
        }

        AppLog.HeavyTestLeft(_logger, name);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task WaitForSensorsAsync(CancellationToken cancellationToken)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Check()
        {
            if (_sensors.State is SensorServiceState.Running or SensorServiceState.Failed or SensorServiceState.Paused)
            {
                ready.TrySetResult();
            }
        }

        void OnStateChanged(object? sender, EventArgs e) => Check();

        _sensors.StateChanged += OnStateChanged;
        try
        {
            Check();
            await ready.Task.WaitAsync(SensorWait, _clock, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            AppLog.SensorsSlowToOpen(_logger, ex);
        }
        finally
        {
            _sensors.StateChanged -= OnStateChanged;
        }
    }
}

public sealed class HeavyTestLease : IDisposable
{
    private readonly HeavyTestGate _owner;
    private readonly IDisposable _subscription;
    private readonly IDisposable _pause;
    private readonly string _name;
    private int _released;

    internal HeavyTestLease(HeavyTestGate owner, IDisposable subscription, IDisposable pause, string name)
    {
        _owner = owner;
        _subscription = subscription;
        _pause = pause;
        _name = name;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        _pause.Dispose();
        _subscription.Dispose();
        _owner.Leave(_name);
    }
}
