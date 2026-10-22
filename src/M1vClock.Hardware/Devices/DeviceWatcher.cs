using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Devices;

public interface IDeviceChangeSource : IDisposable
{
    event EventHandler? DevicesChanged;

    event EventHandler<VolumeEvent>? VolumeChanged;

    bool IsActive { get; }

    void Start();
}

public interface IDeviceWatcher : IDisposable
{
    event EventHandler<DeviceBatch>? DevicesChanged;

    event EventHandler<VolumeEvent>? VolumeChanged;

    bool IsActive { get; }

    Task StartAsync(CancellationToken cancellationToken);
}

public sealed class DeviceWatcher : IDeviceWatcher
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan VolumeWindow = TimeSpan.FromSeconds(4);

    private readonly IUsbDeviceService _usb;
    private readonly IDeviceChangeSource _source;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeviceWatcher> _logger;
    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private IReadOnlyList<UsbDeviceInfo>? _snapshot;
    private DateTimeOffset _lastVolume = DateTimeOffset.MinValue;
    private bool _started;
    private bool _disposed;

    public DeviceWatcher(IUsbDeviceService usb, IDeviceChangeSource source, TimeProvider clock, ILogger<DeviceWatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(usb);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _usb = usb;
        _source = source;
        _clock = clock;
        _logger = logger;
    }

    public event EventHandler<DeviceBatch>? DevicesChanged;

    public event EventHandler<VolumeEvent>? VolumeChanged;

    public bool IsActive => _started && _source.IsActive;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _source.DevicesChanged += OnDevicesChanged;
        _source.VolumeChanged += OnVolumeChanged;
        _source.Start();
        await ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        ITimer? timer;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            timer = _timer;
            _timer = null;
        }

        _source.DevicesChanged -= OnDevicesChanged;
        _source.VolumeChanged -= OnVolumeChanged;
        timer?.Dispose();
        _source.Dispose();
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_timer is null)
            {
                _timer = _clock.CreateTimer(_ => _ = ProcessAsync(), null, Debounce, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnVolumeChanged(object? sender, VolumeEvent e)
    {
        lock (_gate)
        {
            _lastVolume = e.At;
        }

        VolumeChanged?.Invoke(this, e);
    }

    private async Task ProcessAsync()
    {
        var batch = await ReadAsync(CancellationToken.None).ConfigureAwait(false);
        if (batch is null || batch.Diff.IsEmpty)
        {
            return;
        }

        HardwareLog.DeviceBatchDetected(_logger, batch.Diff.Added.Count, batch.Diff.Removed.Count);
        DevicesChanged?.Invoke(this, batch);
    }

    private async Task<DeviceBatch?> ReadAsync(CancellationToken cancellationToken)
    {
        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _usb.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!result.TryGet(out var current))
            {
                return null;
            }

            var previous = _snapshot;
            _snapshot = current;
            if (previous is null)
            {
                return null;
            }

            var now = _clock.GetUtcNow();
            DateTimeOffset lastVolume;
            lock (_gate)
            {
                lastVolume = _lastVolume;
            }

            return new DeviceBatch(DeviceChangeDetector.Diff(previous, current), now - lastVolume < VolumeWindow, now);
        }
        finally
        {
            _reading.Release();
        }
    }
}
