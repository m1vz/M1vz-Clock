using System.Windows;
using System.Windows.Interop;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Games;
using M1vClock.App.Services;
using M1vClock.Games.Frames;
using M1vClock.Games.Overlay;
using M1vClock.Games.Settings;
using M1vClock.Hardware.Sensors;

namespace M1vClock.App.Overlay;

public enum HotkeyState
{
    NotAttached,
    Registered,
    Invalid,
    InUse,
    Failed,
}

public interface IOverlaySurface
{
    bool IsShown { get; }

    void Show();

    void Hide();

    void Apply(OverlayView view, double scale, OverlayCorner corner, IntPtr gameWindow);
}

public interface IOverlayHotkeys
{
    event EventHandler? Pressed;

    bool IsAttached { get; }

    HotkeyState Register(OverlayHotkey hotkey);

    void Unregister();
}

public interface IOverlayForeground
{
    (IntPtr Window, int ProcessId) Current();
}

public static class OverlaySensorMapper
{
    public static OverlaySensors From(SensorSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new OverlaySensors();
        }

        float? Gpu(SensorKind kind, string name) =>
            snapshot.Readings.FirstOrDefault(r => r.Hardware == SensorHardware.Gpu && r.Kind == kind && r.Name == name)?.Value;
        float? GpuStartingWith(SensorKind kind, string name) =>
            snapshot.Readings.FirstOrDefault(r => r.Hardware == SensorHardware.Gpu && r.Kind == kind && r.Name.StartsWith(name, StringComparison.Ordinal) && r.Value is not null)?.Value;

        return new OverlaySensors(
            GpuLoad: snapshot.GpuLoad,
            GpuTemperature: snapshot.GpuTemperature,
            GpuHotSpot: Gpu(SensorKind.Temperature, "GPU Hot Spot"),
            GpuClock: snapshot.GpuClock,
            GpuMemoryClock: Gpu(SensorKind.Clock, "GPU Memory"),
            GpuPower: snapshot.GpuPower,
            GpuFanRpm: GpuStartingWith(SensorKind.Fan, "GPU Fan"),
            GpuFanPercent: GpuStartingWith(SensorKind.Control, "GPU Fan"),
            VramUsedMb: Gpu(SensorKind.SmallData, "GPU Memory Used") ?? Gpu(SensorKind.SmallData, "D3D Dedicated Memory Used"),
            VramTotalMb: Gpu(SensorKind.SmallData, "GPU Memory Total") ?? Gpu(SensorKind.SmallData, "D3D Dedicated Memory Total"),
            CpuLoad: snapshot.CpuLoad,
            CpuMaxCore: BottleneckSampler.BusiestCore(snapshot),
            CpuTemperature: snapshot.CpuTemperature,
            CpuClockMhz: snapshot.CpuClock,
            CpuPower: snapshot.CpuPower,
            RamUsedGb: snapshot.MemoryUsedGb,
            RamLoad: snapshot.MemoryLoad);
    }
}

public sealed class OverlayController : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private readonly ISensorService _sensors;
    private readonly IFrameMonitor _frames;
    private readonly IGameSettings _settings;
    private readonly IOverlaySurface _surface;
    private readonly IOverlayHotkeys _hotkeys;
    private readonly IOverlayForeground _foreground;
    private readonly IUiThread _ui;
    private readonly TimeProvider _time;
    private IDisposable? _sensorLease;
    private IDisposable? _frameLease;
    private ITimer? _timer;
    private int _gameProcess;
    private IntPtr _gameWindow;

    public OverlayController(ISensorService sensors, IFrameMonitor frames, IGameSettings settings, IOverlaySurface surface, IOverlayHotkeys hotkeys, IOverlayForeground foreground, IUiThread ui, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(foreground);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(time);
        _sensors = sensors;
        _frames = frames;
        _settings = settings;
        _surface = surface;
        _hotkeys = hotkeys;
        _foreground = foreground;
        _ui = ui;
        _time = time;
        _hotkeys.Pressed += (_, _) => _ui.Post(Toggle);
    }

    public event EventHandler? Changed;

    public bool IsShown => _surface.IsShown;

    public HotkeyState HotkeyState { get; private set; } = HotkeyState.NotAttached;

    public OverlayView? Latest { get; private set; }

    public FrameMonitorState FrameState => _frames.State;

    public void Start()
    {
        var saved = _settings.Read(s => s.Overlay.Hotkey);
        HotkeyState = OverlayHotkey.Parse(saved) is { } hotkey ? _hotkeys.Register(hotkey) : HotkeyState.Invalid;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public HotkeyState ApplyHotkey(string text)
    {
        if (OverlayHotkey.Parse(text) is not { } hotkey)
        {
            return HotkeyState.Invalid;
        }

        if (!_hotkeys.IsAttached)
        {
            _settings.Update(s => s.Overlay.Hotkey = hotkey.ToString());
            return HotkeyState.NotAttached;
        }

        _hotkeys.Unregister();
        var state = _hotkeys.Register(hotkey);
        if (state == HotkeyState.Registered)
        {
            _settings.Update(s => s.Overlay.Hotkey = hotkey.ToString());
            HotkeyState = state;
        }
        else
        {
            HotkeyState = OverlayHotkey.Parse(_settings.Read(s => s.Overlay.Hotkey)) is { } previous ? _hotkeys.Register(previous) : state;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return state;
    }

    public void Toggle()
    {
        if (IsShown)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        if (IsShown)
        {
            return;
        }

        _sensorLease ??= _sensors.Subscribe();
        _frameLease ??= _frames.Acquire();
        _surface.Show();
        Tick();
        _timer ??= _time.CreateTimer(_ => _ui.Post(Tick), null, Interval, Interval);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Hide()
    {
        _timer?.Dispose();
        _timer = null;
        _surface.Hide();
        _frameLease?.Dispose();
        _frameLease = null;
        _sensorLease?.Dispose();
        _sensorLease = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh()
    {
        if (IsShown)
        {
            Tick();
        }
    }

    public OverlayView Compose()
    {
        var (window, process) = _foreground.Current();
        if (process != 0 && process != Environment.ProcessId)
        {
            _gameProcess = process;
            _gameWindow = window;
        }

        var frames = _gameProcess == 0 ? null : _frames.GetStats(_gameProcess);
        var items = _settings.Read(s => OverlayMetrics.Normalize(s.Overlay.Items));
        var texts = new OverlayTexts(Loc.Get(FrameState == FrameMonitorState.NeedsAdministrator ? "Game.Overlay.NeedsAdmin" : "Game.Overlay.Waiting"), Loc.Get("Game.Overlay.Core"), Loc.Get("Game.Overlay.Memory"), Loc.Get("Game.Overlay.Fan"));
        return OverlayComposer.Compose(items, frames, OverlaySensorMapper.From(_sensors.Latest), _time.GetLocalNow(), texts, Loc.Culture);
    }

    public void Dispose()
    {
        Hide();
        _hotkeys.Unregister();
    }

    private void Tick()
    {
        if (!IsShown)
        {
            return;
        }

        var view = Compose();
        Latest = view;
        var (scale, corner) = _settings.Read(s => (s.Overlay.Scale, s.Overlay.Corner));
        _surface.Apply(view, scale, corner, _gameWindow);
    }
}

public sealed class WpfOverlaySurface : IOverlaySurface
{
    private OverlayWindow? _window;

    public bool IsShown => _window?.IsVisible == true;

    public void Show()
    {
        _window ??= new OverlayWindow();
        _window.Show();
    }

    public void Hide() => _window?.Hide();

    public void Apply(OverlayView view, double scale, OverlayCorner corner, IntPtr gameWindow)
    {
        if (_window is null)
        {
            return;
        }

        _window.Corner = corner;
        _window.GameWindow = gameWindow;
        _window.Apply(view, scale);
    }
}

public sealed class WindowsOverlayForeground : IOverlayForeground
{
    public (IntPtr Window, int ProcessId) Current() => OverlayNative.Foreground();
}

public sealed class WindowHotkeys : IOverlayHotkeys
{
    private const int Id = 0x6D31;
    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? Pressed;

    public bool IsAttached => _source is not null;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(Hook);
        window.Closed += (_, _) => Unregister();
    }

    public HotkeyState Register(OverlayHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (_source is null)
        {
            return HotkeyState.NotAttached;
        }

        if (OverlayNative.Register(_source.Handle, Id, (int)hotkey.Modifiers, hotkey.VirtualKey, out var error))
        {
            _registered = true;
            return HotkeyState.Registered;
        }

        return error == OverlayNative.ErrorHotkeyAlreadyRegistered ? HotkeyState.InUse : HotkeyState.Failed;
    }

    public void Unregister()
    {
        if (_source is null || !_registered)
        {
            return;
        }

        OverlayNative.Unregister(_source.Handle, Id);
        _registered = false;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == OverlayNative.WmHotkey && wParam.ToInt32() == Id)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }
}
