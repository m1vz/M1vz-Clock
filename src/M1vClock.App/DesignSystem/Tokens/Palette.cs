using System.Windows;
using System.Windows.Media;

namespace M1vClock.App.DesignSystem.Tokens;

public enum PaletteKind
{
    Light,
    Dark,
    HighContrast,
}

public sealed record Palette(
    PaletteKind Kind,
    Color WindowBackground,
    Color ChromeFallback,
    Color Surface,
    Color SurfaceElevated,
    Color Separator,
    Color ControlStroke,
    Color FillSubtle,
    Color SidebarSelection,
    Color FillPrimary,
    Color FillPrimaryHover,
    Color FillPrimaryPressed,
    Color FillTrack,
    Color FillSkeleton,
    Color ControlThumb,
    Color TextPrimary,
    Color TextSecondary,
    Color TextTertiary,
    Color TextOnAccent,
    Color Accent,
    Color AccentHover,
    Color AccentPressed,
    Color AccentForeground,
    Color AccentSubtle,
    Color Positive,
    Color Warning,
    Color Critical,
    Color PositiveSubtle,
    Color WarningSubtle,
    Color CriticalSubtle,
    Color CriticalFill,
    Color CriticalFillHover,
    Color CriticalFillPressed,
    Color Scrim,
    Color Shadow,
    Color CaptionCloseHover,
    Color ChartFill)
{
    public static Palette Light { get; } = new(
        PaletteKind.Light,
        WindowBackground: Hex("#F6F6F8"),
        ChromeFallback: Hex("#ECECEF"),
        Surface: Hex("#FFFFFF"),
        SurfaceElevated: Hex("#FFFFFF"),
        Separator: Hex("#E3E3E8"),
        ControlStroke: Hex("#8A8A93"),
        FillSubtle: Hex("#0D000000"),
        SidebarSelection: Hex("#14000000"),
        FillPrimary: Hex("#ECECF0"),
        FillPrimaryHover: Hex("#E4E4E9"),
        FillPrimaryPressed: Hex("#DADAE0"),
        FillTrack: Hex("#E3E3E8"),
        FillSkeleton: Hex("#ECECF0"),
        ControlThumb: Hex("#FFFFFF"),
        TextPrimary: Hex("#1C1C1F"),
        TextSecondary: Hex("#55555D"),
        TextTertiary: Hex("#6C6C75"),
        TextOnAccent: Hex("#FFFFFF"),
        Accent: Hex("#0A5CD6"),
        AccentHover: Hex("#0952C2"),
        AccentPressed: Hex("#0848AD"),
        AccentForeground: Hex("#0A5CD6"),
        AccentSubtle: Hex("#1A0A5CD6"),
        Positive: Hex("#187533"),
        Warning: Hex("#965900"),
        Critical: Hex("#C4282F"),
        PositiveSubtle: Hex("#1F1A7F37"),
        WarningSubtle: Hex("#24E8A317"),
        CriticalSubtle: Hex("#1FC4282F"),
        CriticalFill: Hex("#C4282F"),
        CriticalFillHover: Hex("#AE2128"),
        CriticalFillPressed: Hex("#981C22"),
        Scrim: Hex("#33000000"),
        Shadow: Hex("#000000"),
        CaptionCloseHover: Hex("#C42B1C"),
        ChartFill: Hex("#140A5CD6"));

    public static Palette Dark { get; } = new(
        PaletteKind.Dark,
        WindowBackground: Hex("#161618"),
        ChromeFallback: Hex("#1D1D20"),
        Surface: Hex("#1F1F23"),
        SurfaceElevated: Hex("#29292E"),
        Separator: Hex("#303036"),
        ControlStroke: Hex("#7E7E88"),
        FillSubtle: Hex("#0FFFFFFF"),
        SidebarSelection: Hex("#1AFFFFFF"),
        FillPrimary: Hex("#2D2D33"),
        FillPrimaryHover: Hex("#36363D"),
        FillPrimaryPressed: Hex("#3F3F47"),
        FillTrack: Hex("#38383F"),
        FillSkeleton: Hex("#2B2B31"),
        ControlThumb: Hex("#4A4A53"),
        TextPrimary: Hex("#F2F2F5"),
        TextSecondary: Hex("#B4B4BD"),
        TextTertiary: Hex("#90909A"),
        TextOnAccent: Hex("#FFFFFF"),
        Accent: Hex("#2569D9"),
        AccentHover: Hex("#2F75E6"),
        AccentPressed: Hex("#1F5CC2"),
        AccentForeground: Hex("#6AA6FF"),
        AccentSubtle: Hex("#332569D9"),
        Positive: Hex("#3CCB7F"),
        Warning: Hex("#F2B53D"),
        Critical: Hex("#FF6E6E"),
        PositiveSubtle: Hex("#263CCB7F"),
        WarningSubtle: Hex("#26F2B53D"),
        CriticalSubtle: Hex("#26FF6E6E"),
        CriticalFill: Hex("#D53A3A"),
        CriticalFillHover: Hex("#DF4747"),
        CriticalFillPressed: Hex("#C23030"),
        Scrim: Hex("#73000000"),
        Shadow: Hex("#000000"),
        CaptionCloseHover: Hex("#C42B1C"),
        ChartFill: Hex("#1F6AA6FF"));

    public static Palette HighContrast()
    {
        var window = SystemColors.WindowColor;
        var text = SystemColors.WindowTextColor;
        var highlight = SystemColors.HighlightColor;
        var highlightText = SystemColors.HighlightTextColor;
        var gray = SystemColors.GrayTextColor;
        var hot = SystemColors.HotTrackColor;
        return new Palette(
            PaletteKind.HighContrast,
            window, window, window, window, text, text,
            Colors.Transparent, highlight, window, highlight, highlight, window, gray, highlight,
            text, text, gray, highlightText,
            highlight, highlight, highlight, hot, highlight,
            text, text, text, window, window, window,
            highlight, highlight, highlight,
            Colors.Transparent, Colors.Transparent, highlight, Colors.Transparent);
    }

    public IEnumerable<(string Name, Color Color)> Tokens()
    {
        yield return (nameof(WindowBackground), WindowBackground);
        yield return (nameof(ChromeFallback), ChromeFallback);
        yield return (nameof(Surface), Surface);
        yield return (nameof(SurfaceElevated), SurfaceElevated);
        yield return (nameof(Separator), Separator);
        yield return (nameof(ControlStroke), ControlStroke);
        yield return (nameof(FillSubtle), FillSubtle);
        yield return (nameof(SidebarSelection), SidebarSelection);
        yield return (nameof(FillPrimary), FillPrimary);
        yield return (nameof(FillPrimaryHover), FillPrimaryHover);
        yield return (nameof(FillPrimaryPressed), FillPrimaryPressed);
        yield return (nameof(FillTrack), FillTrack);
        yield return (nameof(FillSkeleton), FillSkeleton);
        yield return (nameof(ControlThumb), ControlThumb);
        yield return (nameof(TextPrimary), TextPrimary);
        yield return (nameof(TextSecondary), TextSecondary);
        yield return (nameof(TextTertiary), TextTertiary);
        yield return (nameof(TextOnAccent), TextOnAccent);
        yield return (nameof(Accent), Accent);
        yield return (nameof(AccentHover), AccentHover);
        yield return (nameof(AccentPressed), AccentPressed);
        yield return (nameof(AccentForeground), AccentForeground);
        yield return (nameof(AccentSubtle), AccentSubtle);
        yield return (nameof(Positive), Positive);
        yield return (nameof(Warning), Warning);
        yield return (nameof(Critical), Critical);
        yield return (nameof(PositiveSubtle), PositiveSubtle);
        yield return (nameof(WarningSubtle), WarningSubtle);
        yield return (nameof(CriticalSubtle), CriticalSubtle);
        yield return (nameof(CriticalFill), CriticalFill);
        yield return (nameof(CriticalFillHover), CriticalFillHover);
        yield return (nameof(CriticalFillPressed), CriticalFillPressed);
        yield return (nameof(Scrim), Scrim);
        yield return (nameof(Shadow), Shadow);
        yield return (nameof(CaptionCloseHover), CaptionCloseHover);
        yield return (nameof(ChartFill), ChartFill);
    }

    public static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);
}
