using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Frames;

public interface IFrameMonitor
{
    FrameMonitorState State { get; }

    IDisposable Acquire();

    FrameStats? GetStats(int processId);

    FrameStats? GetForegroundStats();
}

public sealed class FrameMonitor(IFrameEventSource source, IProcessNames names, IForegroundWindow foreground, TimeProvider clock, ILogger<FrameMonitor> logger) : IFrameMonitor, IDisposable
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly Dictionary<int, FrameWindow> _windows = [];
    private readonly Dictionary<int, long> _lastSeen = [];
    private int _leases;
    private bool _attached;
    private FrameMonitorState _state = FrameMonitorState.Stopped;

    public FrameMonitorState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public IDisposable Acquire()
    {
        lock (_gate)
        {
            _leases++;
            if (_leases == 1)
            {
                StartLocked();
            }
        }

        return new Lease(this);
    }

    public FrameStats? GetStats(int processId)
    {
        FrameStats? stats;
        lock (_gate)
        {
            if (!_windows.TryGetValue(processId, out var window) || !_lastSeen.TryGetValue(processId, out var seen) || clock.GetElapsedTime(seen) > StaleAfter)
            {
                return null;
            }

            stats = window.Compute(processId, string.Empty);
        }

        return stats is null ? null : stats with { ProcessName = names.NameOf(processId) };
    }

    public FrameStats? GetForegroundStats() =>
        foreground.ProcessId() is { } id && id != 0 && id != Environment.ProcessId ? GetStats(id) : null;

    public void Dispose()
    {
        lock (_gate)
        {
            StopLocked();
            _leases = 0;
        }

        source.Dispose();
    }

    private void StartLocked()
    {
        if (!_attached)
        {
            source.Presented += OnPresented;
            _attached = true;
        }

        _state = source.Start() switch
        {
            FrameSourceResult.Started => FrameMonitorState.Running,
            FrameSourceResult.NeedsAdministrator => FrameMonitorState.NeedsAdministrator,
            _ => FrameMonitorState.Failed,
        };
        FramesLog.MonitorStarted(logger, _state);
    }

    private void StopLocked()
    {
        if (_attached)
        {
            source.Presented -= OnPresented;
            _attached = false;
        }

        if (_state == FrameMonitorState.Running)
        {
            source.Shutdown();
        }

        _state = FrameMonitorState.Stopped;
        _windows.Clear();
        _lastSeen.Clear();
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_leases > 0 && --_leases == 0)
            {
                StopLocked();
            }
        }
    }

    private void OnPresented(FramePresent present)
    {
        var now = clock.GetTimestamp();
        lock (_gate)
        {
            if (!_windows.TryGetValue(present.ProcessId, out var window))
            {
                window = new FrameWindow();
                _windows[present.ProcessId] = window;
            }

            window.Add(present.TimestampMs);
            _lastSeen[present.ProcessId] = now;
            Forget(now);
        }
    }

    private void Forget(long now)
    {
        if (_lastSeen.Count < 2)
        {
            return;
        }

        foreach (var id in _lastSeen.Where(p => clock.GetElapsedTime(p.Value, now) > ForgetAfter).Select(p => p.Key).ToList())
        {
            _lastSeen.Remove(id);
            _windows.Remove(id);
        }
    }

    private sealed class Lease(FrameMonitor owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
