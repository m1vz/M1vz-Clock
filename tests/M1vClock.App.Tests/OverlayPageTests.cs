using M1vClock.App.Overlay;
using M1vClock.App.Pages.Games;
using M1vClock.Games.Frames;
using M1vClock.Games.Overlay;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.App.Tests;

internal sealed class FakeSurface : IOverlaySurface
{
    public bool IsShown { get; private set; }

    public List<(OverlayView View, double Scale, OverlayCorner Corner, IntPtr Game)> Applied { get; } = [];

    public void Show() => IsShown = true;

    public void Hide() => IsShown = false;

    public void Apply(OverlayView view, double scale, OverlayCorner corner, IntPtr gameWindow) => Applied.Add((view, scale, corner, gameWindow));
}

internal sealed class FakeHotkeys : IOverlayHotkeys
{
    public event EventHandler? Pressed;

    public bool IsAttached { get; set; } = true;

    public HashSet<string> Taken { get; } = new(StringComparer.Ordinal);

    public List<string> Registered { get; } = [];

    public int Unregistered { get; private set; }

    public HotkeyState Register(OverlayHotkey hotkey)
    {
        if (Taken.Contains(hotkey.ToString()))
        {
            return HotkeyState.InUse;
        }

        Registered.Add(hotkey.ToString());
        return HotkeyState.Registered;
    }

    public void Unregister() => Unregistered++;

    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeForeground : IOverlayForeground
{
    public (IntPtr Window, int ProcessId) Value { get; set; }

    public (IntPtr Window, int ProcessId) Current() => Value;
}

public sealed class OverlayControllerTests
{
    private static readonly FrameStats Cs2 = new(4321, "cs2", 187.4, 5.34, 121.6, [5.1, 5.3]);

    private sealed record Rig(OverlayController Controller, FakeSurface Surface, FakeHotkeys Hotkeys, FakeForeground Foreground, FakeSensorService Sensors, FakeFrames Frames, MemoryGameSettings Settings, FakeTimeProvider Time);

    private static Rig Create()
    {
        var surface = new FakeSurface();
        var hotkeys = new FakeHotkeys();
        var foreground = new FakeForeground();
        var sensors = new FakeSensorService { Latest = Snapshots.Idle() };
        var frames = new FakeFrames();
        var settings = new MemoryGameSettings();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 21, 0, 0, TimeSpan.Zero));
        var controller = new OverlayController(sensors, frames, settings, surface, hotkeys, foreground, new InlineUiThread(), time);
        return new Rig(controller, surface, hotkeys, foreground, sensors, frames, settings, time);
    }

    [Fact]
    public void ShowingTakesTheSensorsAndFramesAndHidingGivesThemBack()
    {
        var rig = Create();

        rig.Controller.Show();
        Assert.True(rig.Controller.IsShown);
        Assert.Equal((1, 1), (rig.Sensors.Subscriptions, rig.Frames.Acquired));
        Assert.Single(rig.Surface.Applied);

        rig.Controller.Hide();
        Assert.False(rig.Surface.IsShown);
        Assert.Equal((1, 1), (rig.Sensors.Releases, rig.Frames.Released));
    }

    [Fact]
    public void WhileShownItRefreshesTwiceASecondAndStopsWhenHidden()
    {
        var rig = Create();
        rig.Controller.Show();

        rig.Time.Advance(TimeSpan.FromSeconds(2));
        var whileShown = rig.Surface.Applied.Count;
        rig.Controller.Hide();
        rig.Time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(5, whileShown);
        Assert.Equal(5, rig.Surface.Applied.Count);
    }

    [Fact]
    public void TheGameIsTheLastForegroundWindowThatIsNotM1vClock()
    {
        var rig = Create();
        rig.Frames.Foreground = Cs2;
        rig.Foreground.Value = (new IntPtr(77), 4321);
        rig.Controller.Show();
        rig.Foreground.Value = (new IntPtr(5), Environment.ProcessId);
        rig.Time.Advance(OverlayController.Interval);

        var last = rig.Surface.Applied[^1];
        Assert.Equal(new IntPtr(77), last.Game);
        Assert.Equal("187", last.View.FpsText);
        Assert.Equal("cs2", last.View.Footer);
    }

    [Fact]
    public void BeforeAnyGameItWaitsAndSaysWhenFpsNeedsAdministrator()
    {
        var rig = Create();
        rig.Frames.State = FrameMonitorState.NeedsAdministrator;

        rig.Controller.Show();

        Assert.Equal("FPS exige administrador", rig.Surface.Applied[0].View.LowText);
        Assert.Equal("0%  42 °C  — MHz  — W", rig.Surface.Applied[0].View.Lines[0].Value);
    }

    [Fact]
    public void CornerScaleAndItemsComeFromTheSettings()
    {
        var rig = Create();
        rig.Settings.Update(s =>
        {
            s.Overlay.Corner = OverlayCorner.BottomRight;
            s.Overlay.Scale = 1.5;
            s.Overlay.Items = [OverlayMetrics.Fps];
        });

        rig.Controller.Show();

        var applied = rig.Surface.Applied[0];
        Assert.Equal((OverlayCorner.BottomRight, 1.5), (applied.Corner, applied.Scale));
        Assert.Empty(applied.View.Lines);
    }

    [Fact]
    public void TheHotkeyTogglesTheOverlay()
    {
        var rig = Create();
        rig.Controller.Start();

        rig.Hotkeys.Press();
        Assert.True(rig.Controller.IsShown);
        rig.Hotkeys.Press();
        Assert.False(rig.Controller.IsShown);
        Assert.Equal(["Ctrl+Shift+F10"], rig.Hotkeys.Registered);
        Assert.Equal(HotkeyState.Registered, rig.Controller.HotkeyState);
    }

    [Fact]
    public void ANewHotkeyIsSavedOnlyWhenWindowsAcceptsIt()
    {
        var rig = Create();
        rig.Controller.Start();
        rig.Hotkeys.Taken.Add("Alt+O");

        Assert.Equal(HotkeyState.Invalid, rig.Controller.ApplyHotkey("O"));
        Assert.Equal(HotkeyState.InUse, rig.Controller.ApplyHotkey("alt+o"));
        Assert.Equal("Ctrl+Shift+F10", rig.Settings.Read(s => s.Overlay.Hotkey));
        Assert.Equal(HotkeyState.Registered, rig.Controller.HotkeyState);
        Assert.Equal(["Ctrl+Shift+F10", "Ctrl+Shift+F10"], rig.Hotkeys.Registered);

        Assert.Equal(HotkeyState.Registered, rig.Controller.ApplyHotkey("shift+f9"));
        Assert.Equal("Shift+F9", rig.Settings.Read(s => s.Overlay.Hotkey));
    }

    [Fact]
    public void DisposingHidesAndReleasesTheHotkey()
    {
        var rig = Create();
        rig.Controller.Start();
        rig.Controller.Show();

        rig.Controller.Dispose();

        Assert.False(rig.Surface.IsShown);
        Assert.True(rig.Hotkeys.Unregistered > 0);
    }

    [Theory]
    [InlineData(OverlayCorner.TopLeft, 12, 12)]
    [InlineData(OverlayCorner.TopRight, 1730, 12)]
    [InlineData(OverlayCorner.BottomLeft, 12, 948)]
    [InlineData(OverlayCorner.BottomRight, 1730, 948)]
    public void TheOverlayIsPlacedInTheChosenCornerOfTheMonitor(OverlayCorner corner, int x, int y) =>
        Assert.Equal((x, y), OverlayPlacement.Position(corner, 0, 0, 1920, 1080, 178, 120, 1.0));

    [Fact]
    public void OnTheSecondMonitorTheMarginScalesWithDpi() =>
        Assert.Equal((1938, -79), OverlayPlacement.Position(OverlayCorner.TopLeft, 1920, -97, 3840, 983, 178, 120, 1.5));

    [Fact]
    public void TheRealIdleReadingOfThisPcMapsToTheOverlayFields()
    {
        var sensors = OverlaySensorMapper.From(Snapshots.Idle());

        Assert.Equal(9.87f, sensors.CpuLoad);
        Assert.Equal(23.97f, sensors.CpuMaxCore);
        Assert.Equal(1455, sensors.VramUsedMb);
        Assert.Equal(8176, sensors.VramTotalMb);
        Assert.Equal(42, sensors.GpuTemperature);
        Assert.Equal(67.949036f, sensors.RamLoad);
        Assert.Equal(new OverlaySensors(), OverlaySensorMapper.From(null));
    }
}

public sealed class OverlayPageTests
{
    private static (OverlayPageViewModel Page, OverlayController Controller, MemoryGameSettings Settings, FakeSurface Surface, FakeTimeProvider Time) Open()
    {
        var surface = new FakeSurface();
        var settings = new MemoryGameSettings();
        var sensors = new FakeSensorService { Latest = Snapshots.Idle() };
        var frames = new FakeFrames { State = FrameMonitorState.NeedsAdministrator };
        var time = new FakeTimeProvider();
        var ui = new InlineUiThread();
        var controller = new OverlayController(sensors, frames, settings, surface, new FakeHotkeys(), new FakeForeground(), ui, time);
        controller.Start();
        var page = new OverlayPageViewModel(new OverlayServices(controller, settings, sensors, frames, ui, time));
        page.OnNavigatedTo();
        return (page, controller, settings, surface, time);
    }

    [Fact]
    public void TheItemsAreGroupedAndTheDefaultsAreOn()
    {
        var (page, _, _, _, _) = Open();

        Assert.Equal(["Quadros", "Placa de vídeo", "Memória de vídeo", "Processador", "Memória RAM", "Outros"], page.Groups.Select(g => g.Title));
        var frames = page.Groups[0].Items;
        Assert.Equal(["FPS", "Tempo de quadro", "1% low", "Gráfico do tempo de quadro"], frames.Select(i => i.Label));
        Assert.All(frames, i => Assert.True(i.IsChecked));
        Assert.False(page.Groups[1].Items.Single(i => i.Key == OverlayMetrics.GpuHotSpot).IsChecked);
    }

    [Fact]
    public void TurningAnItemOffIsSavedAndThePreviewFollows()
    {
        var (page, _, settings, _, _) = Open();

        page.Groups[1].Items.Single(i => i.Key == OverlayMetrics.GpuLoad).IsChecked = false;

        Assert.DoesNotContain(OverlayMetrics.GpuLoad, settings.Read(s => s.Overlay.Items)!);
        Assert.Equal("42 °C  — MHz  — W", page.Preview!.Lines[0].Value);
    }

    [Fact]
    public void PresetsReplaceTheChoiceAndTheTogglesFollow()
    {
        var (page, _, settings, _, _) = Open();

        page.UseMinimal();
        Assert.Equal(OverlayMetrics.Minimal(), settings.Read(s => s.Overlay.Items));
        Assert.Equal(3, page.Groups.SelectMany(g => g.Items).Count(i => i.IsChecked));

        page.UseEverything();
        Assert.All(page.Groups.SelectMany(g => g.Items), i => Assert.True(i.IsChecked));
    }

    [Fact]
    public void CornerAndSizeAreSavedAndTheButtonTogglesTheOverlay()
    {
        var (page, controller, settings, surface, _) = Open();

        page.Toggle();
        Assert.True(controller.IsShown);
        Assert.Equal("Esconder overlay", page.ToggleText);
        page.CornerIndex = 3;
        page.ScaleIndex = 3;

        Assert.Equal((OverlayCorner.BottomRight, 1.5), settings.Read(s => (s.Overlay.Corner, s.Overlay.Scale)));
        Assert.Equal((OverlayCorner.BottomRight, 1.5), (surface.Applied[^1].Corner, surface.Applied[^1].Scale));
        page.Toggle();
        Assert.Equal("Mostrar overlay", page.ToggleText);
    }

    [Fact]
    public void WithoutAdministratorThePageSaysFpsWillBeMissingAndTheHotkeyStateIsShown()
    {
        var (page, _, _, _, time) = Open();

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.StartsWith("Sem administrador, o overlay mostra os sensores", page.FramesNote, StringComparison.Ordinal);
        Assert.Equal("Atalho ativo.", page.HotkeyStatus);
        page.HotkeyText = "Q";
        page.ApplyHotkey();
        Assert.StartsWith("Atalho inválido", page.HotkeyStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavingThePageReleasesItsOwnSensorAndFrameLeases()
    {
        var (page, _, _, _, time) = Open();
        page.OnNavigatedFrom();

        var before = page.Preview;
        time.Advance(TimeSpan.FromSeconds(3));

        Assert.Same(before, page.Preview);
    }
}
