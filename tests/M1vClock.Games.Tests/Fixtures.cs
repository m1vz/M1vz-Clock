using System.IO.Enumeration;
using M1vClock.Games.Library;

namespace M1vClock.Games.Tests;

internal sealed class MemoryFiles : IGameFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? ThrowOnEnumerate { get; set; }

    public MemoryFiles Add(string path, string text)
    {
        var full = Normalize(path);
        _files[full] = text;
        var directory = Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(directory);
            directory = Path.GetDirectoryName(directory);
        }

        return this;
    }

    public MemoryFiles AddDirectory(string path)
    {
        var directory = Normalize(path);
        while (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(directory);
            directory = Path.GetDirectoryName(directory);
        }

        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public string ReadAllText(string path)
    {
        var full = Normalize(path);
        if (Unreadable.Contains(full))
        {
            throw new IOException("Arquivo travado.");
        }

        return _files.TryGetValue(full, out var text) ? text : throw new FileNotFoundException(path);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
    {
        if (ThrowOnEnumerate is not null && Normalize(directory).StartsWith(Normalize(ThrowOnEnumerate), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Disco com problema.");
        }

        var folder = Normalize(directory);
        return _files.Keys
            .Where(f => string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase))
            .Where(f => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(f), ignoreCase: true))
            .ToList();
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');
}

internal sealed class FixedLocations(string? steam = null, string? epic = null, string? riot = null) : IGameLocations
{
    public string? SteamFolder { get; } = steam;

    public string? EpicManifestsFolder { get; } = epic;

    public string? RiotInstallsFile { get; } = riot;
}

internal static class Real
{
    public const string SteamFolder = @"C:\Program Files (x86)\Steam";

    public const string SecondLibrary = @"D:\SteamLibrary";

    public const string EpicFolder = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";

    public const string RiotFile = @"C:\ProgramData\Riot Games\RiotClientInstalls.json";

    public const string LibraryFolders = """
        "libraryfolders"
        {
            "0"
            {
                "path"		"C:\\Program Files (x86)\\Steam"
                "label"		""
                "contentid"		"3866690245117730824"
                "totalsize"		"0"
                "update_clean_bytes_tally"		"641309826"
                "time_last_update_verified"		"1792051756"
                "apps"
                {
                    "730"		"73922650178"
                    "228980"		"181249391"
                    "431960"		"826275581"
                    "3139440"		"1639209630"
                    "3240220"		"103219693669"
                    "3678970"		"664693648"
                }
            }
            "1"
            {
                "path"		"D:\\SteamLibrary"
                "label"		""
                "contentid"		"7082450783364826130"
                "totalsize"		"1000203087872"
                "update_clean_bytes_tally"		"2149344736"
                "time_last_update_verified"		"1792000163"
                "apps"
                {
                    "227300"		"38177898547"
                    "271590"		"129049797551"
                    "632810"		"17768275"
                    "824270"		"7415823499"
                    "1395560"		"119380438"
                    "1415140"		"191770668"
                    "1422450"		"39013448358"
                    "1905180"		"488582775"
                    "2050650"		"73859672758"
                }
            }
        }
        """;

    public const string Cs2Manifest = """
        "AppState"
        {
            "appid"		"730"
            "Universe"		"1"
            "LauncherPath"		"C:\\Program Files (x86)\\Steam\\steam.exe"
            "name"		"Counter-Strike 2"
            "StateFlags"		"6"
            "installdir"		"Counter-Strike Global Offensive"
            "LastUpdated"		"1792061410"
            "LastPlayed"		"1790043894"
            "SizeOnDisk"		"73922650178"
            "StagingSize"		"0"
            "buildid"		"25947352"
            "LastOwner"		"76561190000000000"
            "InstalledDepots"
            {
                "732"
                {
                    "manifest"		"1792491388350933147"
                    "size"		"8"
                }
                "2347770"
                {
                    "manifest"		"2312433566686574487"
                    "size"		"65027793761"
                }
            }
            "SharedDepots"
            {
                "228988"		"228980"
            }
            "UserConfig"
            {
                "language"		"brazilian"
                "DisabledDLC"		"2279721"
            }
        }
        """;

    public static string Manifest(string appId, string name, string installDir, int flags, long size) => $$"""
        "AppState"
        {
            "appid"		"{{appId}}"
            "name"		"{{name}}"
            "StateFlags"		"{{flags}}"
            "installdir"		"{{installDir}}"
            "SizeOnDisk"		"{{size}}"
        }
        """;

    public const string Fortnite = """
        {
            "FormatVersion": 0,
            "bIsIncompleteInstall": false,
            "AppVersionString": "++Fortnite+Release",
            "LaunchExecutable": "FortniteGame/Binaries/Win64/FortniteBootstrapper.exe",
            "InstallLocation": "C:\\Program Files\\Epic Games\\Fortnite",
            "DisplayName": "Fortnite",
            "AppName": "Fortnite",
            "MainGameAppName": "Fortnite",
            "InstallSize": 37704736207,
            "AppCategories": ["games", "applications"]
        }
        """;

    public const string RocketLeague = """
        {
            "FormatVersion": 0,
            "bIsIncompleteInstall": false,
            "LaunchExecutable": "Binaries/Win64/Launcher.exe",
            "InstallLocation": "C:\\Program Files\\Epic Games\\rocketleague",
            "DisplayName": "Rocket League\u00ae",
            "AppName": "Sugar",
            "MainGameAppName": "Sugar",
            "InstallSize": 43679198548,
            "AppCategories": ["public", "games", "applications"]
        }
        """;

    public const string RiotInstalls = """
        {
            "associated_client": {
                "C:/Riot Games/VALORANT/live/": "C:/Riot Games/Riot Client/RiotClientServices.exe"
            },
            "patchlines": {
                "KeystoneFoundationLiveWin": "C:/Riot Games/Riot Client/RiotClientServices.exe"
            },
            "rc_default": "C:/Riot Games/Riot Client/RiotClientServices.exe",
            "rc_live": "C:/Riot Games/Riot Client/RiotClientServices.exe"
        }
        """;
}
