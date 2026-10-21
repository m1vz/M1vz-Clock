using System.Windows;

namespace M1vClock.App.DesignSystem.Tokens;

public enum OpticalCut
{
    Text,
    Display,
}

public sealed record TypeStyle(string Name, double Size, double LineHeight, FontWeight Weight, OpticalCut Cut);

public static class TypeScale
{
    public static TypeStyle Display { get; } = new(nameof(Display), 34, 40, FontWeights.SemiBold, OpticalCut.Display);

    public static TypeStyle Title { get; } = new(nameof(Title), 22, 28, FontWeights.SemiBold, OpticalCut.Display);

    public static TypeStyle Headline { get; } = new(nameof(Headline), 17, 24, FontWeights.SemiBold, OpticalCut.Text);

    public static TypeStyle Body { get; } = new(nameof(Body), 15, 22, FontWeights.Normal, OpticalCut.Text);

    public static TypeStyle Subhead { get; } = new(nameof(Subhead), 13, 18, FontWeights.Normal, OpticalCut.Text);

    public static TypeStyle Caption { get; } = new(nameof(Caption), 11, 14, FontWeights.Medium, OpticalCut.Text);

    public static IReadOnlyList<TypeStyle> All { get; } = [Display, Title, Headline, Body, Subhead, Caption];

    public static IReadOnlyList<FontWeight> Weights { get; } = [FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold];
}
