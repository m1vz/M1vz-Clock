using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Hardware.Devices;
using M1vClock.Hardware.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Services;

public sealed class DeviceNotifier : IHostedService
{
    public static readonly TimeSpan MountDelay = TimeSpan.FromMilliseconds(800);

    private const string DevicesIcon = "Icon.Devices";
    private const string StorageIcon = "Icon.Storage";

    private readonly IDeviceWatcher _watcher;
    private readonly IStorageService _storage;
    private readonly INotificationService _toasts;
    private readonly ActivityLog _activity;
    private readonly AppSettingsService _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeviceNotifier> _logger;
    private readonly Lock _gate = new();
    private IReadOnlyList<Volume> _volumes = [];

    public DeviceNotifier(
        IDeviceWatcher watcher,
        IStorageService storage,
        INotificationService toasts,
        ActivityLog activity,
        AppSettingsService settings,
        TimeProvider clock,
        ILogger<DeviceNotifier> logger)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _watcher = watcher;
        _storage = storage;
        _toasts = toasts;
        _activity = activity;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _watcher.DevicesChanged += OnDevicesChanged;
        _watcher.VolumeChanged += OnVolumeChanged;
        _ = Task.Run(() => InitializeAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher.DevicesChanged -= OnDevicesChanged;
        _watcher.VolumeChanged -= OnVolumeChanged;
        _watcher.Dispose();
        return Task.CompletedTask;
    }

    public static ToastRequest UsbMessage(DeviceChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var device = change.Device;
        var connected = change.Kind == DeviceChangeKind.Connected;
        var text = device.Maker is { } maker && !device.Name.Contains(maker, StringComparison.OrdinalIgnoreCase)
            ? Loc.Format("Hw.Dev.Toast.Maker", device.Name, maker)
            : device.Name;
        return new ToastRequest(
            Loc.Get(connected ? "Hw.Dev.Toast.UsbConnected" : "Hw.Dev.Toast.UsbDisconnected"),
            text,
            DevicesIcon,
            connected ? ToastTone.Success : ToastTone.Info);
    }

    public static ToastRequest VolumeArrivedMessage(string letter, Volume? volume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(letter);
        if (volume is null)
        {
            return new ToastRequest(Loc.Get("Hw.Dev.Toast.Drive"), letter, StorageIcon, ToastTone.Success);
        }

        var title = volume.Kind switch
        {
            VolumeKind.Removable => "Hw.Dev.Toast.PenDrive",
            VolumeKind.External => "Hw.Dev.Toast.External",
            _ => "Hw.Dev.Toast.Drive",
        };
        var text = Loc.Format("Hw.Dev.Toast.VolumeText", Name(volume.Letter, volume.Label), Units.DecimalBytes(volume.FreeBytes), Units.DecimalBytes(volume.TotalBytes));
        return new ToastRequest(Loc.Get(title), text, StorageIcon, ToastTone.Success);
    }

    public static ToastRequest VolumeRemovedMessage(string letter, Volume? known)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(letter);
        var text = known is null
            ? Loc.Format("Hw.Dev.Toast.LetterRemoved", letter)
            : Loc.Format("Hw.Dev.Toast.SafeToRemove", Name(known.Letter, known.Label));
        return new ToastRequest(Loc.Get("Hw.Dev.Toast.DriveRemoved"), text, StorageIcon);
    }

    private static string Name(string letter, string label) => string.IsNullOrWhiteSpace(label) ? letter : $"{label} ({letter})";

    private static bool SameLetter(string left, string right) =>
        string.Equals(left.TrimEnd('\\'), right.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _watcher.StartAsync(cancellationToken).ConfigureAwait(false);
            if (!_watcher.IsActive)
            {
                AppLog.DeviceWatcherUnavailable(_logger);
            }

            await RefreshVolumesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.DeviceNotifierFailed(_logger, ex);
        }
    }

    private void OnDevicesChanged(object? sender, DeviceBatch batch)
    {
        foreach (var change in DeviceChangeDetector.Announcements(batch))
        {
            Announce(change.At, UsbMessage(change));
        }
    }

    private void OnVolumeChanged(object? sender, VolumeEvent volume) => _ = HandleVolumeAsync(volume);

    private async Task HandleVolumeAsync(VolumeEvent volume)
    {
        try
        {
            if (volume.Arrived)
            {
                await Task.Delay(MountDelay, _clock, CancellationToken.None).ConfigureAwait(false);
                await RefreshVolumesAsync(CancellationToken.None).ConfigureAwait(false);
                Announce(volume.At, VolumeArrivedMessage(volume.DriveLetter, Find(volume.DriveLetter)));
                return;
            }

            var known = Find(volume.DriveLetter);
            Announce(volume.At, VolumeRemovedMessage(volume.DriveLetter, known));
            await RefreshVolumesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.DeviceNotifierFailed(_logger, ex);
        }
    }

    private Volume? Find(string letter)
    {
        lock (_gate)
        {
            return _volumes.FirstOrDefault(v => SameLetter(v.Letter, letter));
        }
    }

    private async Task RefreshVolumesAsync(CancellationToken cancellationToken)
    {
        var report = await _storage.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!report.Volumes.TryGet(out var volumes))
        {
            return;
        }

        lock (_gate)
        {
            _volumes = volumes;
        }
    }

    private void Announce(DateTimeOffset at, ToastRequest message)
    {
        _activity.Add(new ActivityEntry(at, message.IconKey, message.Title, message.Text));
        if (_settings.Current.NotifyDevices)
        {
            _toasts.Show(message);
        }
    }
}
