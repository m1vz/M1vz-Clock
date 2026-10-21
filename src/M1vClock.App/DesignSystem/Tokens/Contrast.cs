using System.Windows.Media;

namespace M1vClock.App.DesignSystem.Tokens;

public static class Contrast
{
    public static double Ratio(Color foreground, Color background)
    {
        var visible = Composite(foreground, background);
        var a = RelativeLuminance(visible);
        var b = RelativeLuminance(background);
        var (light, dark) = a > b ? (a, b) : (b, a);
        return (light + 0.05) / (dark + 0.05);
    }

    public static Color Composite(Color top, Color bottom)
    {
        var alpha = top.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(top.R * alpha + bottom.R * (1 - alpha)),
            (byte)Math.Round(top.G * alpha + bottom.G * (1 - alpha)),
            (byte)Math.Round(top.B * alpha + bottom.B * (1 - alpha)));
    }

    public static double RelativeLuminance(Color color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

    private static double Channel(byte value)
    {
        var c = value / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
