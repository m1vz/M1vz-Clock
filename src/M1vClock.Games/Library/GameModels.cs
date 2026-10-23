using System.Text.RegularExpressions;

namespace M1vClock.Games.Library;

public enum GamePlatform
{
    Steam,
    Epic,
    Riot,
}

public enum GameState
{
    Installed,
    UpdatePending,
    Updating,
    NeedsRepair,
    Incomplete,
}

public sealed partial record GameLaunch(string Target, string? Arguments)
{
    [GeneratedRegex(@"^[0-9]{1,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    public static GameLaunch? Steam(string appId) =>
        Digits().IsMatch(appId) ? new GameLaunch("steam://rungameid/" + appId, null) : null;

    public static GameLaunch? Epic(string appName) =>
        Token().IsMatch(appName) ? new GameLaunch("com.epicgames.launcher://apps/" + appName + "?action=launch&silent=true", null) : null;

    public static GameLaunch? Riot(string clientPath, string product, string patchline)
    {
        if (!Path.IsPathFullyQualified(clientPath) || !Token().IsMatch(product) || !Token().IsMatch(patchline))
        {
            return null;
        }

        return new GameLaunch(clientPath, $"--launch-product={product} --launch-patchline={patchline}");
    }
}

public sealed record GameEntry(
    string Id,
    string Name,
    GamePlatform Platform,
    string? InstallFolder,
    long? SizeBytes,
    GameState State,
    GameLaunch? Launch)
{
    public bool IsHealthy => State is GameState.Installed or GameState.UpdatePending;

    public bool FolderExists { get; init; }
}

public enum GameSort
{
    Name,
    Size,
}

public sealed record GameLibrary(IReadOnlyList<GameEntry> Games, IReadOnlyList<GamePlatform> Failed)
{
    public long KnownBytes => Games.Sum(g => g.SizeBytes ?? 0);

    public int NeedAttention => Games.Count(g => !g.IsHealthy);

    public int Platforms => Games.Select(g => g.Platform).Distinct().Count();
}

public interface IGameFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    string ReadAllText(string path);

    IEnumerable<string> EnumerateFiles(string directory, string pattern);
}

public interface IGameLocations
{
    string? SteamFolder { get; }

    string? EpicManifestsFolder { get; }

    string? RiotInstallsFile { get; }
}

public interface IGameLibraryService
{
    Task<GameLibrary> ReadAsync(CancellationToken cancellationToken);
}
