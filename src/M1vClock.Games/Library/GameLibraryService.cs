using M1vClock.Core.RegistryValues;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace M1vClock.Games.Library;

public sealed class WindowsGameFileSystem : IGameFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public IEnumerable<string> EnumerateFiles(string directory, string pattern) =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly) : [];
}

public sealed class WindowsGameLocations(IRegistryAccess registry) : IGameLocations
{
    public string? SteamFolder
    {
        get
        {
            var current = registry.Read(new RegistryValueAddress(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Valve\Steam", "SteamPath"));
            if (current.ValueExists && current.Data is string path && path.Length > 0)
            {
                return Existing(path);
            }

            var machine = registry.Read(new RegistryValueAddress(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath"));
            return machine.ValueExists && machine.Data is string install && install.Length > 0 ? Existing(install) : null;
        }
    }

    public string? EpicManifestsFolder => ExistingFolder(Path.Combine(Data(), "Epic", "EpicGamesLauncher", "Data", "Manifests"));

    public string? RiotInstallsFile
    {
        get
        {
            var file = Path.Combine(Data(), "Riot Games", "RiotClientInstalls.json");
            return File.Exists(file) ? file : null;
        }
    }

    private static string Data() => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private static string? Existing(string path)
    {
        var full = Path.GetFullPath(path);
        return Directory.Exists(full) ? full : null;
    }

    private static string? ExistingFolder(string path) => Directory.Exists(path) ? path : null;
}

public sealed class GameLibraryService(IGameFileSystem files, IGameLocations locations, ILogger<GameLibraryService> logger) : IGameLibraryService
{
    public Task<GameLibrary> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    public static IReadOnlyList<GameEntry> Sort(IReadOnlyList<GameEntry> games, GameSort sort)
    {
        ArgumentNullException.ThrowIfNull(games);
        return sort == GameSort.Size
            ? games.OrderByDescending(g => g.SizeBytes ?? -1).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
            : games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Platform).ToList();
    }

    private GameLibrary Read(CancellationToken cancellationToken)
    {
        var games = new List<GameEntry>();
        var failed = new List<GamePlatform>();
        Collect(GamePlatform.Steam, locations.SteamFolder, folder => new SteamLibraryReader(files, logger).Read(folder), games, failed, cancellationToken);
        Collect(GamePlatform.Epic, locations.EpicManifestsFolder, folder => new EpicLibraryReader(files, logger).Read(folder), games, failed, cancellationToken);
        Collect(GamePlatform.Riot, locations.RiotInstallsFile, file => new RiotLibraryReader(files, logger).Read(file), games, failed, cancellationToken);
        GamesLog.Read(logger, games.Count);
        var withFolders = games.Select(g => g with { FolderExists = g.InstallFolder is not null && files.DirectoryExists(g.InstallFolder) }).ToList();
        return new GameLibrary(Sort(withFolders, GameSort.Name), failed);
    }

    private void Collect(GamePlatform platform, string? location, Func<string, IReadOnlyList<GameEntry>> read, List<GameEntry> games, List<GamePlatform> failed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (location is null)
        {
            return;
        }

        try
        {
            games.AddRange(read(location));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            GamesLog.LibraryFailed(logger, ex, platform);
            failed.Add(platform);
        }
    }
}
