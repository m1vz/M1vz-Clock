using M1vClock.Games.Settings;

namespace M1vClock.Games.Library;

public enum ManualGameProblem
{
    None,
    NotAnExecutable,
    Missing,
    UnsafeArguments,
    AlreadyAdded,
}

public static class ManualGames
{
    public const string IdPrefix = "manual:";
    public const int MaximumName = 120;

    public static IReadOnlyList<GameEntry> Entries(IReadOnlyList<ManualGame> manual, IGameFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(manual);
        ArgumentNullException.ThrowIfNull(files);
        var games = new List<GameEntry>();
        foreach (var game in manual)
        {
            if (!GameLaunch.IsExecutablePath(game.ExePath))
            {
                continue;
            }

            var exists = files.FileExists(game.ExePath);
            var name = NameFor(game.Name, game.ExePath);
            var launch = exists ? GameLaunch.Executable(game.ExePath, game.Arguments) : null;
            games.Add(new GameEntry(
                IdPrefix + game.Id.ToString("N"),
                name,
                GamePlatform.Manual,
                Path.GetDirectoryName(Path.GetFullPath(game.ExePath)),
                null,
                exists ? GameState.Installed : GameState.Incomplete,
                launch));
        }

        return games;
    }

    public static ManualGameProblem Check(IReadOnlyList<ManualGame> existing, string exePath, string? arguments, IGameFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(files);
        if (!GameLaunch.IsExecutablePath(exePath))
        {
            return ManualGameProblem.NotAnExecutable;
        }

        if (!files.FileExists(exePath))
        {
            return ManualGameProblem.Missing;
        }

        if (!GameLaunch.IsSafeArguments(arguments))
        {
            return ManualGameProblem.UnsafeArguments;
        }

        var full = Path.GetFullPath(exePath);
        return existing.Any(g => string.Equals(Path.GetFullPath(g.ExePath), full, StringComparison.OrdinalIgnoreCase))
            ? ManualGameProblem.AlreadyAdded
            : ManualGameProblem.None;
    }

    public static string NameFor(string? name, string exePath)
    {
        var text = (name ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            text = Path.GetFileNameWithoutExtension(exePath);
        }

        return text.Length > MaximumName ? text[..MaximumName] : text;
    }

    public static Guid? IdOf(GameEntry game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Platform == GamePlatform.Manual && game.Id.StartsWith(IdPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(game.Id[IdPrefix.Length..], "N", out var id)
            ? id
            : null;
    }
}
