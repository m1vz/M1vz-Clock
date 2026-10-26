using System.Globalization;
using M1vClock.Games.Frames;
using M1vClock.Games.Overlay;

namespace M1vClock.Games.Tests;

public sealed class OverlayComposerTests
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly OverlayTexts Texts = new("aguardando jogo", "núcleo", "mem", "fan");
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 21, 5, 0, TimeSpan.FromHours(-3));

    private static readonly OverlaySensors ThisPcPlaying = new(
        GpuLoad: 97, GpuTemperature: 63, GpuHotSpot: 78, GpuClock: 1845, GpuMemoryClock: 1750, GpuPower: 118, GpuFanRpm: 1650,
        VramUsedMb: 5734, VramTotalMb: 8176, CpuLoad: 31, CpuMaxCore: 74, CpuTemperature: 58, CpuClockMhz: 4612, CpuPower: 62, RamUsedGb: 14.2f, RamLoad: 44);

    private static readonly FrameStats Cs2 = new(4321, "cs2", 187.4, 5.34, 121.6, [5.1, 5.3, 12.0, 5.2]);

    [Fact]
    public void TheDefaultItemsGiveFourLinesAndTheFrameNumbers()
    {
        var view = OverlayComposer.Compose(OverlayMetrics.Defaults(), Cs2, ThisPcPlaying, Now, Texts, Portuguese);

        Assert.Equal("187", view.FpsText);
        Assert.Equal("5,3 ms", view.FrameTimeText);
        Assert.Equal("1% low 122", view.LowText);
        Assert.Equal(
        [
            new OverlayLine("GPU", "97%  63 °C  1845 MHz  118 W"),
            new OverlayLine("VRAM", "5,6 GB"),
            new OverlayLine("CPU", "31%  58 °C  4,61 GHz  62 W"),
            new OverlayLine("RAM", "14,2 GB"),
        ], view.Lines);
        Assert.Equal("cs2", view.Footer);
        Assert.True(view.ShowGraph);
        Assert.Equal(4, view.Graph.Count);
    }

    [Fact]
    public void EverythingAddsTheExtraReadingsAndTheClock()
    {
        var view = OverlayComposer.Compose(OverlayMetrics.Everything(), Cs2, ThisPcPlaying, Now, Texts, Portuguese);

        Assert.Equal("97%  63 °C  HS 78 °C  1845 MHz  mem 1750 MHz  118 W  1650 RPM", view.Lines[0].Value);
        Assert.Equal("5,6 GB  70%", view.Lines[1].Value);
        Assert.Equal("31%  núcleo 74%  58 °C  4,61 GHz  62 W", view.Lines[2].Value);
        Assert.Equal("14,2 GB  44%", view.Lines[3].Value);
        Assert.Equal("cs2  ·  21:05", view.Footer);
    }

    [Fact]
    public void TheMinimalPresetShowsOnlyFrameNumbers()
    {
        var view = OverlayComposer.Compose(OverlayMetrics.Minimal(), Cs2, ThisPcPlaying, Now, Texts, Portuguese);

        Assert.Empty(view.Lines);
        Assert.True(view.ShowFps && view.ShowFrameTime && view.ShowLow);
        Assert.False(view.ShowGraph);
        Assert.False(view.HasFooter);
    }

    [Fact]
    public void WithoutAGameAndWithoutSensorsItWaitsAndShowsDashes()
    {
        var view = OverlayComposer.Compose(OverlayMetrics.Defaults(), null, null, Now, Texts, Portuguese);

        Assert.Equal("—", view.FpsText);
        Assert.Equal("aguardando jogo", view.LowText);
        Assert.Equal("—  —  — MHz  — W", view.Lines[0].Value);
        Assert.Empty(view.Graph);
        Assert.False(view.HasFooter);
    }

    [Fact]
    public void AZeroTemperatureMeansNoReadingAndTheFanFallsBackToPercent()
    {
        var sensors = ThisPcPlaying with { CpuTemperature = 0, GpuFanRpm = 0, GpuFanPercent = 35 };

        var view = OverlayComposer.Compose([OverlayMetrics.CpuTemperature, OverlayMetrics.GpuFan], Cs2, sensors, Now, Texts, CultureInfo.InvariantCulture);

        Assert.Equal(new OverlayLine("GPU", "fan 35%"), view.Lines[0]);
        Assert.Equal(new OverlayLine("CPU", "—"), view.Lines[1]);
    }

    [Fact]
    public void TheGraphScaleKeepsNormalFramesLowAndSpikesHigh()
    {
        var graph = OverlayComposer.Graph([5, 5, 5, 20]);

        Assert.Equal([0.25, 0.25, 0.25, 1.0], graph);
        Assert.Empty(OverlayComposer.Graph([5]));
        Assert.Equal([0.0, 0.0], OverlayComposer.Graph([0, 0]));
    }

    [Fact]
    public void ItemsAreNormalizedToTheCatalogOrderAndUnknownKeysDropped()
    {
        Assert.Equal([OverlayMetrics.Fps, OverlayMetrics.CpuLoad], OverlayMetrics.Normalize(["cpu-load", "inventado", "fps", "fps"]));
        Assert.Equal(OverlayMetrics.Defaults(), OverlayMetrics.Normalize(null));
    }
}

public sealed class OverlayHotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Shift+F10", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x79, "Ctrl+Shift+F10")]
    [InlineData("shift + ctrl + f10", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x79, "Ctrl+Shift+F10")]
    [InlineData("F12", HotkeyModifiers.None, 0x7B, "F12")]
    [InlineData("Alt+O", HotkeyModifiers.Alt, 0x4F, "Alt+O")]
    [InlineData("Ctrl+Alt+Home", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x24, "Ctrl+Alt+Home")]
    [InlineData("Win+5", HotkeyModifiers.Windows, 0x35, "Win+5")]
    public void ValidShortcutsAreParsedAndWrittenBackTheSameWay(string text, HotkeyModifiers modifiers, int key, string canonical)
    {
        var hotkey = OverlayHotkey.Parse(text);

        Assert.NotNull(hotkey);
        Assert.Equal(modifiers, hotkey.Modifiers);
        Assert.Equal(key, hotkey.VirtualKey);
        Assert.Equal(canonical, hotkey.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("O")]
    [InlineData("Ctrl+Ctrl+O")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+O")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Enter")]
    public void ShortcutsThatWouldStealTypingOrDoNotExistAreRefused(string text) =>
        Assert.Null(OverlayHotkey.Parse(text));

    [Fact]
    public void TheDefaultShortcutIsValid() =>
        Assert.Equal(OverlayHotkey.Default, OverlayHotkey.Parse(OverlayHotkey.Default)!.ToString());
}
