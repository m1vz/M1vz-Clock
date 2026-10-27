using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Overlay;
using M1vClock.App.Services;
using M1vClock.Games.Frames;
using M1vClock.Games.Overlay;
using M1vClock.Games.Settings;
using M1vClock.Hardware.Sensors;

namespace M1vClock.App.Pages.Games;

public sealed partial class OverlayItemRow : ObservableObject
{
    private readonly Action<OverlayItemRow> _changed;
    private readonly bool _ready;

    public OverlayItemRow(string key, bool isChecked, Action<OverlayItemRow> changed)
    {
        Key = key;
        Label = Loc.Get("Game.Overlay.Item." + OverlayText.Pascal(key));
        _changed = changed;
        IsChecked = isChecked;
        _ready = true;
    }

    public string Key { get; }

    public string Label { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    public override string ToString() => Label;

    partial void OnIsCheckedChanged(bool value)
    {
        if (_ready)
        {
            _changed(this);
        }
    }
}

public sealed record OverlayItemGroup(string Title, IReadOnlyList<OverlayItemRow> Items);

public static class OverlayText
{
    public static string Pascal(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return string.Concat(key.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    public static string HotkeyResult(HotkeyState state) => Loc.Get("Game.Overlay.Hotkey.State." + state);
}

public sealed record OverlayServices(OverlayController Controller, IGameSettings Settings, ISensorService Sensors, IFrameMonitor Frames, IUiThread Ui, TimeProvider Time);

public sealed partial class OverlayPageViewModel : PageViewModel, IDisposable
{
    private static readonly OverlayCorner[] Corners = [OverlayCorner.TopLeft, OverlayCorner.TopRight, OverlayCorner.BottomLeft, OverlayCorner.BottomRight];
    private static readonly double[] Scales = [0.75, 1.0, 1.25, 1.5, 2.0];

    private readonly OverlayServices _services;
    private readonly bool _ready;
    private IDisposable? _sensorLease;
    private IDisposable? _frameLease;
    private ITimer? _timer;

    public OverlayPageViewModel(OverlayServices services)
        : base(PageCatalog.Find("overlay"))
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        CornerLabels = Corners.Select(c => Loc.Get($"Game.Overlay.Corner.{c}")).ToList();
        ScaleLabels = Scales.Select(s => (s * 100).ToString("0", Loc.Culture) + "%").ToList();
        var (corner, scale, hotkey) = services.Settings.Read(s => (s.Overlay.Corner, s.Overlay.Scale, s.Overlay.Hotkey));
        CornerIndex = Math.Max(0, Array.IndexOf(Corners, corner));
        ScaleIndex = Scales.Select((v, i) => (Distance: Math.Abs(v - scale), Index: i)).MinBy(p => p.Distance).Index;
        HotkeyText = hotkey;
        BuildGroups();
        ShowState();
        _ready = true;
    }

    public IReadOnlyList<string> CornerLabels { get; }

    public IReadOnlyList<string> ScaleLabels { get; }

    public ObservableCollection<OverlayItemGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial bool IsShown { get; set; }

    [ObservableProperty]
    public partial int CornerIndex { get; set; }

    [ObservableProperty]
    public partial int ScaleIndex { get; set; }

    [ObservableProperty]
    public partial string HotkeyText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HotkeyStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FramesNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial OverlayView? Preview { get; set; }

    public bool HasFramesNote => FramesNote.Length > 0;

    public string ToggleText => Loc.Get(IsShown ? "Game.Overlay.Hide" : "Game.Overlay.Show");

    public override void OnNavigatedTo()
    {
        _services.Controller.Changed += OnControllerChanged;
        _sensorLease ??= _services.Sensors.Subscribe();
        _frameLease ??= _services.Frames.Acquire();
        _timer ??= _services.Time.CreateTimer(_ => _services.Ui.Post(UpdatePreview), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        ShowState();
    }

    public override void OnNavigatedFrom() => Stop();

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    public void Toggle()
    {
        _services.Controller.Toggle();
        ShowState();
    }

    [RelayCommand]
    public void UseMinimal() => UsePreset(OverlayMetrics.Minimal());

    [RelayCommand]
    public void UseDefault() => UsePreset(OverlayMetrics.Defaults());

    [RelayCommand]
    public void UseEverything() => UsePreset(OverlayMetrics.Everything());

    [RelayCommand]
    public void ApplyHotkey()
    {
        var state = _services.Controller.ApplyHotkey(HotkeyText);
        if (state is HotkeyState.Registered or HotkeyState.NotAttached)
        {
            HotkeyText = _services.Settings.Read(s => s.Overlay.Hotkey);
        }

        HotkeyStatus = OverlayText.HotkeyResult(state);
    }

    public void UpdatePreview()
    {
        Preview = _services.Controller.Compose();
        FramesNote = _services.Frames.State == FrameMonitorState.NeedsAdministrator ? Loc.Get("Game.Overlay.FramesNeedAdmin") : string.Empty;
    }

    partial void OnCornerIndexChanged(int value)
    {
        if (_ready)
        {
            _services.Settings.Update(s => s.Overlay.Corner = Corners[Math.Clamp(value, 0, Corners.Length - 1)]);
            _services.Controller.Refresh();
        }
    }

    partial void OnScaleIndexChanged(int value)
    {
        if (_ready)
        {
            _services.Settings.Update(s => s.Overlay.Scale = Scales[Math.Clamp(value, 0, Scales.Length - 1)]);
            _services.Controller.Refresh();
        }
    }

    partial void OnIsShownChanged(bool value) => OnPropertyChanged(nameof(ToggleText));

    partial void OnFramesNoteChanged(string value) => OnPropertyChanged(nameof(HasFramesNote));

    private void Stop()
    {
        _services.Controller.Changed -= OnControllerChanged;
        _timer?.Dispose();
        _timer = null;
        _frameLease?.Dispose();
        _frameLease = null;
        _sensorLease?.Dispose();
        _sensorLease = null;
    }

    private void OnControllerChanged(object? sender, EventArgs e) => _services.Ui.Post(ShowState);

    private void ShowState()
    {
        IsShown = _services.Controller.IsShown;
        if (HotkeyStatus.Length == 0 || _services.Controller.HotkeyState != HotkeyState.Registered)
        {
            HotkeyStatus = OverlayText.HotkeyResult(_services.Controller.HotkeyState);
        }
    }

    private void UsePreset(IReadOnlyList<string> keys)
    {
        _services.Settings.Update(s => s.Overlay.Items = keys.ToList());
        BuildGroups();
        _services.Controller.Refresh();
        UpdatePreview();
    }

    private void BuildGroups()
    {
        var chosen = _services.Settings.Read(s => OverlayMetrics.Normalize(s.Overlay.Items)).ToHashSet(StringComparer.Ordinal);
        Groups.Clear();
        foreach (var group in OverlayMetrics.All.GroupBy(m => m.Group))
        {
            Groups.Add(new OverlayItemGroup(
                Loc.Get($"Game.Overlay.Group.{group.Key}"),
                group.Select(m => new OverlayItemRow(m.Key, chosen.Contains(m.Key), OnItemChanged)).ToList()));
        }
    }

    private void OnItemChanged(OverlayItemRow row)
    {
        var chosen = Groups.SelectMany(g => g.Items).Where(i => i.IsChecked).Select(i => i.Key).ToList();
        _services.Settings.Update(s => s.Overlay.Items = OverlayMetrics.Normalize(chosen).ToList());
        _services.Controller.Refresh();
        UpdatePreview();
    }
}
