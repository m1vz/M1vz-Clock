using System.Globalization;
using M1vClock.Games.Library;

namespace M1vClock.Games.Profiles;

public enum GameEngine
{
    Unknown,
    Source,
    Unity,
    Unreal,
}

public enum WindowMode
{
    Default,
    Fullscreen,
    Borderless,
    Windowed,
}

public enum GamePriority
{
    Normal,
    AboveNormal,
    High,
}

public sealed class HelperProgram
{
    public string Path { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;

    public bool CloseWithGame { get; set; } = true;
}

public sealed class GameProfile
{
    public GamePriority Priority { get; set; } = GamePriority.Normal;

    public bool HighPerformanceGpu { get; set; }

    public bool DisableFullscreenOptimizations { get; set; }

    public string? ExePath { get; set; }

    public GameEngine Engine { get; set; } = GameEngine.Unknown;

    public WindowMode WindowMode { get; set; } = WindowMode.Default;

    public int Width { get; set; }

    public int Height { get; set; }

    public int RefreshRate { get; set; }

    public string Extra { get; set; } = string.Empty;

    public List<HelperProgram> Helpers { get; set; } = [];

    public static string KeyOf(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Platform + ":" + game.Id;
    }

    public bool IsEmpty =>
        Priority == GamePriority.Normal && !HighPerformanceGpu && !DisableFullscreenOptimizations && ExePath is null
        && WindowMode == WindowMode.Default && Width == 0 && Height == 0 && RefreshRate == 0 && Extra.Length == 0 && Helpers.Count == 0;

    public GameProfile Copy() => new()
    {
        Priority = Priority,
        HighPerformanceGpu = HighPerformanceGpu,
        DisableFullscreenOptimizations = DisableFullscreenOptimizations,
        ExePath = ExePath,
        Engine = Engine,
        WindowMode = WindowMode,
        Width = Width,
        Height = Height,
        RefreshRate = RefreshRate,
        Extra = Extra,
        Helpers = Helpers.Select(h => new HelperProgram { Path = h.Path, Arguments = h.Arguments, CloseWithGame = h.CloseWithGame }).ToList(),
    };
}

public enum ProfileProblem
{
    None,
    ResolutionOutOfRange,
    RefreshOutOfRange,
    UnsafeExtra,
    BadExecutable,
    BadHelper,
}

public static class LaunchArguments
{
    public const int MinimumSide = 320;
    public const int MaximumSide = 16384;
    public const int MinimumRefresh = 24;
    public const int MaximumRefresh = 1000;

    public static ProfileProblem Validate(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var hasWidth = profile.Width != 0;
        var hasHeight = profile.Height != 0;
        if (hasWidth != hasHeight || (hasWidth && (!InRange(profile.Width, MinimumSide, MaximumSide) || !InRange(profile.Height, MinimumSide, MaximumSide))))
        {
            return ProfileProblem.ResolutionOutOfRange;
        }

        if (profile.RefreshRate != 0 && !InRange(profile.RefreshRate, MinimumRefresh, MaximumRefresh))
        {
            return ProfileProblem.RefreshOutOfRange;
        }

        if (!GameLaunch.IsSafeArguments(profile.Extra))
        {
            return ProfileProblem.UnsafeExtra;
        }

        if (profile.ExePath is not null && !GameLaunch.IsExecutablePath(profile.ExePath))
        {
            return ProfileProblem.BadExecutable;
        }

        return profile.Helpers.Any(h => !GameLaunch.IsExecutablePath(h.Path) || !GameLaunch.IsSafeArguments(h.Arguments))
            ? ProfileProblem.BadHelper
            : ProfileProblem.None;
    }

    public static string Build(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (Validate(profile) != ProfileProblem.None)
        {
            throw new ArgumentException("Perfil inválido.", nameof(profile));
        }

        var parts = new List<string>();
        var width = profile.Width;
        var height = profile.Height;
        switch (profile.Engine)
        {
            case GameEngine.Source:
                parts.AddRange(profile.WindowMode switch
                {
                    WindowMode.Fullscreen => ["-fullscreen"],
                    WindowMode.Windowed => ["-windowed"],
                    WindowMode.Borderless => ["-windowed", "-noborder"],
                    _ => [],
                });
                if (width > 0)
                {
                    parts.Add(Invariant($"-w {width} -h {height}"));
                }

                if (profile.RefreshRate > 0)
                {
                    parts.Add(Invariant($"-freq {profile.RefreshRate}"));
                }

                break;
            case GameEngine.Unity:
                parts.AddRange(profile.WindowMode switch
                {
                    WindowMode.Fullscreen => ["-screen-fullscreen 1", "-window-mode exclusive"],
                    WindowMode.Windowed => ["-screen-fullscreen 0", "-window-mode windowed"],
                    WindowMode.Borderless => ["-popupwindow", "-window-mode borderless"],
                    _ => [],
                });
                if (width > 0)
                {
                    parts.Add(Invariant($"-screen-width {width} -screen-height {height}"));
                }

                break;
            case GameEngine.Unreal:
                parts.AddRange(profile.WindowMode switch
                {
                    WindowMode.Fullscreen => ["-fullscreen"],
                    WindowMode.Windowed or WindowMode.Borderless => ["-windowed"],
                    _ => [],
                });
                if (width > 0)
                {
                    parts.Add(Invariant($"-ResX={width} -ResY={height}"));
                }

                break;
            default:
                break;
        }

        var extra = profile.Extra.Trim();
        if (extra.Length > 0)
        {
            parts.Add(extra);
        }

        return string.Join(' ', parts);
    }

    public static bool UsesEngineOptions(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Engine != GameEngine.Unknown && (profile.WindowMode != WindowMode.Default || profile.Width > 0 || profile.RefreshRate > 0);
    }

    private static bool InRange(int value, int minimum, int maximum) => value >= minimum && value <= maximum;

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

public static class EngineDetector
{
    public static GameEngine Detect(IGameFileSystem files, string? installFolder)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (string.IsNullOrWhiteSpace(installFolder) || !files.DirectoryExists(installFolder))
        {
            return GameEngine.Unknown;
        }

        var folders = new List<string> { installFolder };
        folders.AddRange(files.EnumerateDirectories(installFolder).Take(64));
        foreach (var folder in folders)
        {
            if (files.FileExists(Path.Combine(folder, "UnityPlayer.dll")))
            {
                return GameEngine.Unity;
            }

            if (files.DirectoryExists(Path.Combine(folder, "Engine", "Binaries")))
            {
                return GameEngine.Unreal;
            }

            if (files.FileExists(Path.Combine(folder, "game", "bin", "win64", "engine2.dll"))
                || files.FileExists(Path.Combine(folder, "bin", "engine.dll"))
                || files.FileExists(Path.Combine(folder, "bin", "win64", "engine.dll")))
            {
                return GameEngine.Source;
            }
        }

        return GameEngine.Unknown;
    }
}
