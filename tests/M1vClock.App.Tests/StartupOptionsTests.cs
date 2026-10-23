using System.IO;
using M1vClock.App.Hosting;
using M1vClock.Core.Settings;

namespace M1vClock.App.Tests;

public sealed class StartupOptionsTests
{
    [Fact]
    public void NoArgumentsKeepsTheDefaults()
    {
        var options = StartupOptions.Parse([]);

        Assert.Null(options.Page);
        Assert.Null(options.Theme);
        Assert.Null(options.ScreenshotPath);
        Assert.Null(options.Overlay);
        Assert.Equal(TimeSpan.FromSeconds(3), options.ScreenshotDelay);
        Assert.Equal(0, options.Scroll);
    }

    [Fact]
    public void ReadsPageThemeOverlayAndScroll()
    {
        var options = StartupOptions.Parse(["--page", "settings", "--theme", "dark", "--overlay", "palette", "--scroll", "120.5"]);

        Assert.Equal("settings", options.Page);
        Assert.Equal(ThemePreference.Dark, options.Theme);
        Assert.Equal("palette", options.Overlay);
        Assert.Equal(120.5, options.Scroll);
    }

    [Fact]
    public void FlagsAreCaseInsensitive()
    {
        var options = StartupOptions.Parse(["--PAGE", "history", "--Theme", "LIGHT"]);

        Assert.Equal("history", options.Page);
        Assert.Equal(ThemePreference.Light, options.Theme);
    }

    [Fact]
    public void TheToastOverlayIsAccepted()
    {
        Assert.Equal("toast", StartupOptions.Parse(["--overlay", "toast"]).Overlay);
    }

    [Fact]
    public void UnknownThemeAndOverlayAreIgnored()
    {
        var options = StartupOptions.Parse(["--theme", "roxo", "--overlay", "tudo"]);

        Assert.Null(options.Theme);
        Assert.Null(options.Overlay);
    }

    [Fact]
    public void ScreenshotPathBecomesAbsoluteAndDelayIsClamped()
    {
        var options = StartupOptions.Parse(["--screenshot", "captura.png", "--screenshot-delay", "999"]);

        Assert.True(Path.IsPathRooted(options.ScreenshotPath));
        Assert.EndsWith("captura.png", options.ScreenshotPath, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ScreenshotDelay);
        Assert.Equal(TimeSpan.FromSeconds(0.5), StartupOptions.Parse(["--screenshot-delay", "0"]).ScreenshotDelay);
    }

    [Fact]
    public void RenderOnlyScreenshotsAreOffUnlessAskedFor()
    {
        Assert.False(StartupOptions.Parse(["--screenshot", "captura.png"]).RenderOnly);
        Assert.True(StartupOptions.Parse(["--screenshot", "captura.png", "--screenshot-render"]).RenderOnly);
        Assert.True(StartupOptions.Parse(["--SCREENSHOT-RENDER", "--page", "tweaks"]).RenderOnly);
    }

    [Fact]
    public void FlagWithoutValueAndNegativeScrollAreSafe()
    {
        var options = StartupOptions.Parse(["--scroll", "-50", "--page"]);

        Assert.Equal(0, options.Scroll);
        Assert.Null(options.Page);
    }
}
