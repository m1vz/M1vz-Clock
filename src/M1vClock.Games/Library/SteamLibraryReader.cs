using System.Globalization;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Library;

public sealed class SteamLibraryReader(IGameFileSystem files, ILogger logger)
{
    private const int FullyInstalled = 4;
    private const int UpdateRequired = 2;
    private const int FilesMissing = 32;
    private const int FilesCorrupt = 128;
    private const int Working = 256 | 1024 | 262144 | 524288 | 1048576 | 2097152 | 4194304 | 8388608;

    private static readonly HashSet<string> ToolIds = ["228980"];

    private sealed record Library(string Path, HashSet<string> Listed);

    private sealed record Candidate(string AppId, string Name, string Folder, long? Size, int Flags, bool Listed, bool FolderExists);

    public IReadOnlyList<GameEntry> Read(string steamFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamFolder);
        var candidates = new List<Candidate>();
        foreach (var library in Libraries(steamFolder))
        {
            var steamApps = Path.Combine(library.Path, "steamapps");
            foreach (var manifest in files.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                if (ReadManifest(manifest, steamApps, library) is { } candidate)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates
            .Where(c => !ToolIds.Contains(c.AppId))
            .GroupBy(c => c.AppId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(c => c.FolderExists).ThenByDescending(c => c.Listed).ThenByDescending(c => c.Size ?? 0).First())
            .Select(ToEntry)
            .ToList();
    }

    public static GameState StateOf(int flags)
    {
        if ((flags & (FilesMissing | FilesCorrupt)) != 0)
        {
            return GameState.NeedsRepair;
        }

        if ((flags & Working) != 0)
        {
            return GameState.Updating;
        }

        if ((flags & FullyInstalled) == 0)
        {
            return GameState.Incomplete;
        }

        return (flags & UpdateRequired) != 0 ? GameState.UpdatePending : GameState.Installed;
    }

    private static GameEntry ToEntry(Candidate candidate)
    {
        var state = StateOf(candidate.Flags);
        var launchable = state is GameState.Installed or GameState.UpdatePending;
        return new GameEntry(
            candidate.AppId,
            candidate.Name,
            GamePlatform.Steam,
            candidate.Folder,
            candidate.Size,
            state,
            launchable ? GameLaunch.Steam(candidate.AppId) : null);
    }

    private List<Library> Libraries(string steamFolder)
    {
        var libraries = new List<Library> { new(steamFolder, []) };
        var listing = Path.Combine(steamFolder, "steamapps", "libraryfolders.vdf");
        if (!files.FileExists(listing))
        {
            return libraries;
        }

        VdfNode root;
        try
        {
            root = Vdf.Parse(files.ReadAllText(listing));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            GamesLog.FileSkipped(logger, ex, listing);
            return libraries;
        }

        foreach (var entry in root.Children.Where(c => c.Name.All(char.IsAsciiDigit)))
        {
            var path = entry.IsObject ? entry.Text("path") : entry.Value;
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                continue;
            }

            var listed = entry.Child("apps")?.Children.Select(a => a.Name).ToHashSet(StringComparer.Ordinal) ?? [];
            var index = libraries.FindIndex(l => string.Equals(l.Path.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                libraries.Add(new Library(path, listed));
            }
            else
            {
                libraries[index].Listed.UnionWith(listed);
                libraries[index] = libraries[index] with { Path = path };
            }
        }

        return libraries;
    }

    private Candidate? ReadManifest(string manifest, string steamApps, Library library)
    {
        try
        {
            var state = Vdf.Parse(files.ReadAllText(manifest));
            var appId = state.Text("appid");
            var name = state.Text("name");
            var installDir = state.Text("installdir");
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir) || !appId.All(char.IsAsciiDigit))
            {
                return null;
            }

            var folder = Path.Combine(steamApps, "common", installDir);
            long? size = long.TryParse(state.Text("SizeOnDisk"), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes > 0 ? bytes : null;
            _ = int.TryParse(state.Text("StateFlags"), NumberStyles.None, CultureInfo.InvariantCulture, out var flags);
            return new Candidate(appId, name.Trim(), folder, size, flags, library.Listed.Contains(appId), files.DirectoryExists(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            GamesLog.FileSkipped(logger, ex, manifest);
            return null;
        }
    }
}
