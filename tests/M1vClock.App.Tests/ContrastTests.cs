using System.Windows.Media;
using M1vClock.App.DesignSystem.Tokens;

namespace M1vClock.App.Tests;

public sealed class ContrastTests
{
    private const double TextMinimum = 4.5;
    private const double ControlMinimum = 3.0;

    public static TheoryData<string> PaletteNames => ["Claro", "Escuro"];

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void TextColorsMeetAaOnSurfaceAndWindow(string name)
    {
        var palette = PaletteFor(name);
        var text = new (string Name, Color Color)[]
        {
            (nameof(Palette.TextPrimary), palette.TextPrimary),
            (nameof(Palette.TextSecondary), palette.TextSecondary),
            (nameof(Palette.TextTertiary), palette.TextTertiary),
            (nameof(Palette.AccentForeground), palette.AccentForeground),
            (nameof(Palette.Positive), palette.Positive),
            (nameof(Palette.Warning), palette.Warning),
            (nameof(Palette.Critical), palette.Critical),
        };

        foreach (var (token, color) in text)
        {
            foreach (var background in new[] { palette.Surface, palette.WindowBackground, palette.SurfaceElevated })
            {
                var ratio = Contrast.Ratio(color, background);
                Assert.True(ratio >= TextMinimum, $"{name}: {token} sobre {background} tem contraste {ratio:0.00}:1.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void StateTextOnItsSubtleBackgroundMeetsAa(string name)
    {
        var palette = PaletteFor(name);
        var pairs = new (string Name, Color Text, Color Fill)[]
        {
            ("Accent", palette.AccentForeground, palette.AccentSubtle),
            ("Positive", palette.Positive, palette.PositiveSubtle),
            ("Warning", palette.Warning, palette.WarningSubtle),
            ("Critical", palette.Critical, palette.CriticalSubtle),
        };

        foreach (var (token, text, fill) in pairs)
        {
            var background = Contrast.Composite(fill, palette.Surface);
            var ratio = Contrast.Ratio(text, background);
            Assert.True(ratio >= TextMinimum, $"{name}: {token} sobre o próprio fundo suave tem contraste {ratio:0.00}:1.");
        }
    }

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void ButtonLabelsMeetAaInEveryState(string name)
    {
        var palette = PaletteFor(name);
        var fills = new (string Name, Color Fill)[]
        {
            (nameof(Palette.Accent), palette.Accent),
            (nameof(Palette.AccentHover), palette.AccentHover),
            (nameof(Palette.AccentPressed), palette.AccentPressed),
            (nameof(Palette.CriticalFill), palette.CriticalFill),
            (nameof(Palette.CriticalFillHover), palette.CriticalFillHover),
            (nameof(Palette.CriticalFillPressed), palette.CriticalFillPressed),
        };

        foreach (var (token, fill) in fills)
        {
            var ratio = Contrast.Ratio(palette.TextOnAccent, fill);
            Assert.True(ratio >= TextMinimum, $"{name}: texto sobre {token} tem contraste {ratio:0.00}:1.");
        }
    }

    [Theory]
    [MemberData(nameof(PaletteNames))]
    public void ControlStrokeIsVisibleAgainstTheSurface(string name)
    {
        var palette = PaletteFor(name);

        var ratio = Contrast.Ratio(palette.ControlStroke, palette.Surface);

        Assert.True(ratio >= ControlMinimum, $"{name}: contorno de controle tem contraste {ratio:0.00}:1.");
    }

    [Fact]
    public void BlackOnWhiteIsTwentyOneToOne()
    {
        Assert.Equal(21.0, Contrast.Ratio(Colors.Black, Colors.White), 3);
    }

    [Fact]
    public void RatioIsTheSameInBothDirections()
    {
        var a = Color.FromRgb(10, 92, 214);
        var b = Color.FromRgb(246, 246, 248);

        Assert.Equal(Contrast.Ratio(a, b), Contrast.Ratio(b, a), 6);
    }

    [Fact]
    public void TransparentForegroundTakesTheBackgroundColor()
    {
        var ratio = Contrast.Ratio(Color.FromArgb(0, 0, 0, 0), Colors.White);

        Assert.Equal(1.0, ratio, 3);
    }

    [Fact]
    public void HalfTransparentBlackOverWhiteIsMidGray()
    {
        var composite = Contrast.Composite(Color.FromArgb(128, 0, 0, 0), Colors.White);

        Assert.Equal(127, composite.R);
        Assert.Equal(composite.R, composite.G);
        Assert.Equal(composite.R, composite.B);
    }

    private static Palette PaletteFor(string name) => name == "Claro" ? Palette.Light : Palette.Dark;
}
