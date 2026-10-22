using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Devices;

public sealed class WmiDeviceChangeSource(TimeProvider clock, ILogger<WmiDeviceChangeSource> logger) : IDeviceChangeSource
{
    private const string DeviceQuery = "SELECT * FROM Win32_DeviceChangeEvent";
    private const string VolumeQuery = "SELECT * FROM Win32_VolumeChangeEvent";
    private const ushort VolumeArrival = 2;
    private const ushort VolumeRemoval = 3;

    private ManagementEventWatcher? _devices;
    private ManagementEventWatcher? _volumes;

    public event EventHandler? DevicesChanged;

    public event EventHandler<VolumeEvent>? VolumeChanged;

    public bool IsActive => _devices is not null;

    public void Start()
    {
        _devices ??= Watch(DeviceQuery, (_, _) => DevicesChanged?.Invoke(this, EventArgs.Empty));
        _volumes ??= Watch(VolumeQuery, OnVolume);
    }

    public void Dispose()
    {
        Stop(ref _devices);
        Stop(ref _volumes);
    }

    private static void Stop(ref ManagementEventWatcher? watcher)
    {
        var current = watcher;
        watcher = null;
        current?.Stop();
        current?.Dispose();
    }

    private ManagementEventWatcher? Watch(string query, EventArrivedEventHandler handler)
    {
        ManagementEventWatcher? watcher = null;
        try
        {
            watcher = new ManagementEventWatcher(new WqlEventQuery(query));
            watcher.EventArrived += handler;
            watcher.Start();
            return watcher;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            HardwareLog.OptionalSourceFailed(logger, ex, query);
            watcher?.Dispose();
            return null;
        }
    }

    private void OnVolume(object sender, EventArrivedEventArgs e)
    {
        var letter = e.NewEvent["DriveName"] as string;
        var type = e.NewEvent["EventType"] is ushort value ? value : (ushort)0;
        if (string.IsNullOrWhiteSpace(letter) || type is not (VolumeArrival or VolumeRemoval))
        {
            return;
        }

        VolumeChanged?.Invoke(this, new VolumeEvent(letter, type == VolumeArrival, clock.GetUtcNow()));
    }
}
