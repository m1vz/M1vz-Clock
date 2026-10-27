using System.Text.RegularExpressions;

namespace M1vClock.Games.Estimate;

public sealed record CatalogGame(string Name, string Genre, double GpuReference, double CpuCap, double EngineCap = 0);

public enum FpsLimiter
{
    Gpu,
    Cpu,
    Engine,
}

public enum EstimateResolution
{
    FullHd,
    QuadHd,
    UltraHd,
}

public enum EstimatePreset
{
    Low,
    Medium,
    High,
    Ultra,
}

public enum EstimateUpscaling
{
    Off,
    Quality,
    Performance,
}

public enum FpsRating
{
    Unplayable,
    Weak,
    Playable,
    Great,
    Excellent,
}

public sealed record FpsEstimate(double Average, double Low1, FpsLimiter Limiter)
{
    public FpsRating Rating => Average switch
    {
        >= 144 => FpsRating.Excellent,
        >= 90 => FpsRating.Great,
        >= 60 => FpsRating.Playable,
        >= 30 => FpsRating.Weak,
        _ => FpsRating.Unplayable,
    };
}

public sealed record EstimateSettings(EstimateResolution Resolution, EstimatePreset Preset, EstimateUpscaling Upscaling);

public static partial class FpsEstimator
{
    public static IReadOnlyList<CatalogGame> Games { get; } =
    [
        new("Counter-Strike 2", "FPS competitivo", 340, 420),
        new("Valorant", "FPS competitivo", 600, 650),
        new("Fortnite", "Battle royale", 160, 250),
        new("League of Legends", "MOBA", 420, 480),
        new("Dota 2", "MOBA", 260, 280),
        new("Deadlock", "MOBA de tiro", 170, 230),
        new("Apex Legends", "Battle royale", 220, 300),
        new("PUBG: Battlegrounds", "Battle royale", 180, 220),
        new("Call of Duty: Black Ops 6", "FPS", 140, 220),
        new("Call of Duty: Warzone", "Battle royale", 130, 200),
        new("Battlefield 6", "FPS", 120, 170),
        new("Rainbow Six Siege", "FPS tático", 350, 430),
        new("Overwatch 2", "FPS heroico", 280, 380),
        new("Marvel Rivals", "FPS heroico", 110, 160),
        new("Rocket League", "Esporte", 400, 450),
        new("EA Sports FC 25", "Esporte", 220, 260),
        new("Minecraft", "Sandbox", 300, 340),
        new("GTA V", "Mundo aberto", 160, 190),
        new("Red Dead Redemption 2", "Mundo aberto", 95, 160),
        new("Cyberpunk 2077", "RPG mundo aberto", 95, 150),
        new("The Witcher 3: Wild Hunt", "RPG", 120, 160),
        new("Elden Ring", "RPG de ação", 110, 140, 60),
        new("Baldur's Gate 3", "RPG", 110, 120),
        new("Hogwarts Legacy", "RPG de ação", 90, 120),
        new("Starfield", "RPG espacial", 65, 90),
        new("Black Myth: Wukong", "Ação", 70, 140),
        new("Alan Wake 2", "Terror / ação", 60, 140),
        new("God of War Ragnarök", "Ação", 110, 170),
        new("Resident Evil 4", "Terror / ação", 140, 190),
        new("Forza Horizon 5", "Corrida", 140, 200),
        new("Euro Truck Simulator 2", "Simulação", 200, 160),
        new("Helldivers 2", "Cooperativo", 90, 110),
        new("Monster Hunter Wilds", "Ação", 60, 85),
        new("Assassin's Creed Shadows", "Ação mundo aberto", 65, 110),
        new("Borderlands 4", "Looter shooter", 55, 110),
    ];

    private static readonly (string Key, double Index)[] GpuIndex =
    [
        ("RTX 5090", 265), ("RTX 5080", 175), ("RTX 5070 Ti", 150), ("RTX 5070", 118), ("RTX 5060 Ti", 95), ("RTX 5060", 85),
        ("RTX 4090", 210), ("RTX 4080 SUPER", 160), ("RTX 4080", 157), ("RTX 4070 Ti SUPER", 136), ("RTX 4070 Ti", 124),
        ("RTX 4070 SUPER", 116), ("RTX 4070", 100), ("RTX 4060 Ti", 82), ("RTX 4060", 70),
        ("RTX 3090 Ti", 135), ("RTX 3090", 125), ("RTX 3080 Ti", 122), ("RTX 3080", 112), ("RTX 3070 Ti", 92), ("RTX 3070", 86),
        ("RTX 3060 Ti", 78), ("RTX 3060", 60), ("RTX 3050", 40),
        ("RTX 2080 Ti", 90), ("RTX 2080 SUPER", 77), ("RTX 2080", 74), ("RTX 2070 SUPER", 68), ("RTX 2070", 60), ("RTX 2060 SUPER", 60), ("RTX 2060", 52),
        ("GTX 1660 Ti", 43), ("GTX 1660 SUPER", 42), ("GTX 1660", 38), ("GTX 1650 SUPER", 35), ("GTX 1650", 28),
        ("GTX 1080 Ti", 66), ("GTX 1080", 50), ("GTX 1070 Ti", 45), ("GTX 1070", 40), ("GTX 1060", 28),
        ("RX 9070 XT", 160), ("RX 9070", 140), ("RX 7900 XTX", 170), ("RX 7900 XT", 150), ("RX 7900 GRE", 128),
        ("RX 7800 XT", 115), ("RX 7700 XT", 98), ("RX 7600 XT", 72), ("RX 7600", 70),
        ("RX 6950 XT", 125), ("RX 6900 XT", 118), ("RX 6800 XT", 112), ("RX 6800", 98), ("RX 6750 XT", 86), ("RX 6700 XT", 82), ("RX 6700", 76),
        ("RX 6650 XT", 69), ("RX 6600 XT", 66), ("RX 6600", 58), ("RX 6500 XT", 27),
        ("RX 5700 XT", 62), ("RX 5700", 56), ("RX 5600 XT", 52), ("RX 5500 XT", 36), ("RX 590", 27), ("RX 580", 25),
        ("Arc B580", 80), ("Arc A770", 68), ("Arc A750", 62),
        ("Radeon(TM) Graphics", 6), ("Radeon Graphics", 6),
    ];

    private static readonly (string Key, double Index)[] CpuIndex =
    [
        ("9950X3D", 138), ("9800X3D", 140), ("7950X3D", 126), ("7800X3D", 128), ("5800X3D", 95), ("5700X3D", 90),
        ("9950X", 112), ("9900X", 110), ("9700X", 108), ("9600X", 104), ("7950X", 104), ("7900X", 102), ("7700X", 100),
        ("7700", 97), ("7600X", 96), ("7600", 93), ("5950X", 82), ("5900X", 81), ("5800X", 80), ("5700X", 77), ("5700G", 70),
        ("5600X", 76), ("5600G", 66), ("5600", 73), ("5500", 66),
        ("3900X", 66), ("3700X", 63), ("3600X", 61), ("3600", 60), ("2700X", 50), ("2600", 45),
        ("285K", 112), ("265K", 108), ("245K", 100), ("14900K", 116), ("14700K", 112), ("14600K", 104), ("13900K", 113),
        ("13700K", 108), ("13600K", 101), ("13400", 84), ("12900K", 98), ("12700K", 95), ("12600K", 90), ("12400", 78),
        ("11700K", 74), ("11400", 66), ("10900K", 72), ("10700K", 70), ("10400", 60), ("9900K", 68), ("9700K", 64), ("9400", 52),
    ];

    public static double? GpuIndexOf(string gpuName)
    {
        ArgumentNullException.ThrowIfNull(gpuName);
        return Lookup(GpuIndex, gpuName) ?? Lookup(GpuIndex, ShortGpuName(gpuName));
    }

    public static double? CpuIndexOf(string cpuName)
    {
        ArgumentNullException.ThrowIfNull(cpuName);
        return Lookup(CpuIndex, cpuName);
    }

    public static double ResolutionFactor(EstimateResolution resolution) => resolution switch
    {
        EstimateResolution.QuadHd => 0.72,
        EstimateResolution.UltraHd => 0.42,
        _ => 1.0,
    };

    public static double PresetFactor(EstimatePreset preset) => preset switch
    {
        EstimatePreset.Low => 1.6,
        EstimatePreset.Medium => 1.3,
        EstimatePreset.Ultra => 0.82,
        _ => 1.0,
    };

    public static double UpscalingFactor(EstimateUpscaling upscaling, EstimateResolution resolution) => upscaling switch
    {
        EstimateUpscaling.Quality => resolution == EstimateResolution.FullHd ? 1.25 : 1.4,
        EstimateUpscaling.Performance => resolution == EstimateResolution.FullHd ? 1.45 : 1.75,
        _ => 1.0,
    };

    public static FpsEstimate Estimate(CatalogGame game, double gpuIndex, double cpuIndex, EstimateSettings settings)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gpuIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cpuIndex);
        var gpuFps = game.GpuReference * gpuIndex / 100 * ResolutionFactor(settings.Resolution) * PresetFactor(settings.Preset) * UpscalingFactor(settings.Upscaling, settings.Resolution);
        var cpuFps = game.CpuCap * cpuIndex / 100 * settings.Preset switch
        {
            EstimatePreset.Low => 1.1,
            EstimatePreset.Ultra => 0.95,
            _ => 1.0,
        };
        var fps = Math.Min(gpuFps, cpuFps);
        var limiter = gpuFps <= cpuFps ? FpsLimiter.Gpu : FpsLimiter.Cpu;
        if (game.EngineCap > 0 && game.EngineCap < fps)
        {
            fps = game.EngineCap;
            limiter = FpsLimiter.Engine;
        }

        return new FpsEstimate(Math.Round(fps), Math.Round(fps * 0.75), limiter);
    }

    public static string ShortGpuName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var text = name.Replace("(TM)", string.Empty, StringComparison.Ordinal).Replace("(R)", string.Empty, StringComparison.Ordinal);
        foreach (var word in new[] { "NVIDIA", "GeForce", "AMD", "Intel" })
        {
            text = text.Replace(word, string.Empty, StringComparison.Ordinal);
        }

        if (text.Contains("Radeon RX", StringComparison.Ordinal))
        {
            text = text.Replace("Radeon", string.Empty, StringComparison.Ordinal);
        }

        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.StartsWith("Arc", StringComparison.Ordinal) && text.EndsWith(" Graphics", StringComparison.Ordinal))
        {
            text = text[..^" Graphics".Length];
        }

        return text;
    }

    public static string ShortCpuName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var text = name.Replace("AMD", string.Empty, StringComparison.Ordinal)
            .Replace("Intel(R)", string.Empty, StringComparison.Ordinal)
            .Replace("Core(TM)", "Core", StringComparison.Ordinal)
            .Replace("(R)", string.Empty, StringComparison.Ordinal)
            .Replace("(TM)", string.Empty, StringComparison.Ordinal);
        var core = text.IndexOf("-Core", StringComparison.OrdinalIgnoreCase);
        if (core > 0 && text.LastIndexOf(' ', core) is var space and > 0)
        {
            text = text[..space];
        }

        text = text.Replace("Processor", string.Empty, StringComparison.Ordinal).Replace("CPU", string.Empty, StringComparison.Ordinal);
        text = Generation().Replace(text, string.Empty);
        var at = text.IndexOf('@', StringComparison.Ordinal);
        if (at > 0)
        {
            text = text[..at];
        }

        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        ["gtav"] = ["grandtheftautov"],
        ["pubgbattlegrounds"] = ["pubg"],
        ["callofdutyblackops6"] = ["callofduty"],
        ["callofdutywarzone"] = ["callofduty"],
    };

    public static bool SameGame(string catalogName, string installedName)
    {
        ArgumentNullException.ThrowIfNull(catalogName);
        ArgumentNullException.ThrowIfNull(installedName);
        var catalog = Normalize(catalogName);
        var installed = Normalize(installedName);
        if (catalog.Length < 3 || installed.Length < 3)
        {
            return false;
        }

        if (installed.StartsWith(catalog, StringComparison.Ordinal) || catalog.StartsWith(installed, StringComparison.Ordinal))
        {
            return true;
        }

        return Aliases.TryGetValue(catalog, out var names) && names.Any(n => installed.StartsWith(n, StringComparison.Ordinal));
    }

    private static string Normalize(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static double? Lookup((string Key, double Index)[] table, string name)
    {
        var hit = table.Where(t => name.Contains(t.Key, StringComparison.OrdinalIgnoreCase)).OrderByDescending(t => t.Key.Length).FirstOrDefault();
        return hit.Key is null ? null : hit.Index;
    }

    [GeneratedRegex(@"\d+th Gen\s*", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Generation();
}
