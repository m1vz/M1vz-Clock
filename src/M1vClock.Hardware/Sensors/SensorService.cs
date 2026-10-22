using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Sensors;

public interface ISensorService
{
    SensorServiceState State { get; }

    SensorSnapshot? Latest { get; }

    event EventHandler<SensorSnapshot>? SnapshotReady;

    event EventHandler? StateChanged;

    IDisposable Subscribe();

    IDisposable Pause();
}

public sealed class SensorService : ISensorService, IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private const int LogEveryFailures = 60;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private readonly ISensorSource _source;
    private readonly TimeProvider _clock;
    private readonly ILogger<SensorService> _logger;
    private readonly Lock _gate = new();
    private int _leases;
    private int _failedReads;
    private int _pauses;
    private bool _disposed;
    private bool _opened;
    private bool _failed;
    private CancellationTokenSource? _cancellation;
    private Task _tail = Task.CompletedTask;
    private SensorServiceState _state = SensorServiceState.Stopped;
    private SensorSnapshot? _latest;

    public SensorService(ISensorSource source, TimeProvider clock, ILogger<SensorService> logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _source = source;
        _clock = clock;
        _logger = logger;
    }

    public event EventHandler<SensorSnapshot>? SnapshotReady;

    public event EventHandler? StateChanged;

    public SensorServiceState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public SensorSnapshot? Latest => Volatile.Read(ref _latest);

    public IDisposable Subscribe()
    {
        Change(() => _leases++);
        return new Release(() => Change(() => _leases--));
    }

    public IDisposable Pause()
    {
        Change(() => _pauses++);
        return new Release(() => Change(() => _pauses--));
    }

    public void Dispose()
    {
        Task tail;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellation?.Cancel();
            _cancellation = null;
            tail = _tail;
        }

        if (!tail.Wait(StopTimeout))
        {
            HardwareLog.SensorStopTimedOut(_logger);
        }

        _source.Dispose();
        SetState(SensorServiceState.Stopped);
    }

    private void Change(Action change)
    {
        lock (_gate)
        {
            change();
            Reconcile();
        }
    }

    private void Reconcile()
    {
        if (_disposed)
        {
            return;
        }

        var shouldRun = _leases > 0 && _pauses == 0 && !_failed;
        if (shouldRun && _cancellation is null)
        {
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            var previous = _tail;
            _tail = Task.Run(() => RunAsync(previous, cancellation), CancellationToken.None);
        }
        else if (!shouldRun && _cancellation is not null)
        {
            _cancellation.Cancel();
            _cancellation = null;
        }

        if (!shouldRun)
        {
            SetStateLocked(_failed ? SensorServiceState.Failed : _leases > 0 && _pauses > 0 ? SensorServiceState.Paused : SensorServiceState.Stopped);
        }
    }

    private async Task RunAsync(Task previous, CancellationTokenSource cancellation)
    {
        using (cancellation)
        {
            await previous.ConfigureAwait(false);
            var token = cancellation.Token;
            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (!await OpenAsync().ConfigureAwait(false))
                {
                    return;
                }

                using var timer = new PeriodicTimer(Interval, _clock);
                SetState(SensorServiceState.Running);
                Publish();
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    Publish();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> OpenAsync()
    {
        if (_opened)
        {
            return true;
        }

        SetState(SensorServiceState.Starting);
        var started = _clock.GetTimestamp();
        try
        {
            await Task.Run(_source.Open, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            HardwareLog.SensorOpenFailed(_logger, ex);
            lock (_gate)
            {
                _failed = true;
                _cancellation = null;
            }

            SetState(SensorServiceState.Failed);
            return false;
        }

        _opened = true;
        var elapsed = (long)_clock.GetElapsedTime(started).TotalMilliseconds;
        HardwareLog.SensorsOpened(_logger, elapsed);
        return true;
    }

    private void Publish()
    {
        IReadOnlyList<SensorReading> readings;
        try
        {
            readings = _source.Read();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (_failedReads++ % LogEveryFailures == 0)
            {
                HardwareLog.SensorReadFailed(_logger, ex);
            }

            return;
        }

        _failedReads = 0;
        var snapshot = new SensorSnapshot(_clock.GetUtcNow(), readings);
        Volatile.Write(ref _latest, snapshot);
        SnapshotReady?.Invoke(this, snapshot);
    }

    private void SetState(SensorServiceState state)
    {
        lock (_gate)
        {
            SetStateLocked(state);
        }
    }

    private void SetStateLocked(SensorServiceState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Release(Action release) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                release();
            }
        }
    }
}
