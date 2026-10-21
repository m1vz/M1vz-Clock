using System.IO;
using M1vClock.Core.Settings;

namespace M1vClock.App.Hosting;

public sealed record StartupOptions(string? Page, ThemePreference? Theme, string? ScreenshotPath, TimeSpan ScreenshotDelay, string? Overlay = null, double Scroll = 0)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? page = null;
        ThemePreference? theme = null;
        string? screenshot = null;
        var delay = TimeSpan.FromSeconds(3);
        string? overlay = null;
        double scroll = 0;
        for (var i = 0; i < args.Count; i++)
        {
            var value = i + 1 < args.Count ? args[i + 1] : null;
            switch (args[i].ToUpperInvariant())
            {
                case "--PAGE" when value is not null:
                    page = value;
                    i++;
                    break;
                case "--THEME" when value is not null && Enum.TryParse<ThemePreference>(value, ignoreCase: true, out var parsed):
                    theme = parsed;
                    i++;
                    break;
                case "--SCREENSHOT" when value is not null:
                    screenshot = Path.GetFullPath(value);
                    i++;
                    break;
                case "--SCREENSHOT-DELAY" when value is not null && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var seconds):
                    delay = TimeSpan.FromSeconds(Math.Clamp(seconds, 0.5, 30));
                    i++;
                    break;
                case "--OVERLAY" when value is "palette" or "dialog":
                    overlay = value;
                    i++;
                    break;
                case "--SCROLL" when value is not null && double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var offset):
                    scroll = Math.Max(0, offset);
                    i++;
                    break;
            }
        }

        return new StartupOptions(page, theme, screenshot, delay, overlay, scroll);
    }
}
