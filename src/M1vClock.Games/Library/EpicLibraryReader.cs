using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Library;

public sealed class EpicLibraryReader(IGameFileSystem files, ILogger logger)
{
    public IReadOnlyList<GameEntry> Read(string manifestsFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestsFolder);
        var games = new List<GameEntry>();
        foreach (var item in files.EnumerateFiles(manifestsFolder, "*.item"))
        {
            if (Entry(item) is { } game)
            {
                games.Add(game);
            }
        }

        return games;
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool IsGame(JsonElement root)
    {
        if (!root.TryGetProperty("AppCategories", out var categories) || categories.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return categories.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase));
    }

    private GameEntry? Entry(string item)
    {
        try
        {
            using var document = JsonDocument.Parse(files.ReadAllText(item));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var appName = Text(root, "AppName");
            var name = Text(root, "DisplayName");
            if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(name) || !IsGame(root))
            {
                return null;
            }

            var main = Text(root, "MainGameAppName");
            if (!string.IsNullOrEmpty(main) && !string.Equals(main, appName, StringComparison.Ordinal))
            {
                return null;
            }

            var folder = Text(root, "InstallLocation");
            long? size = root.TryGetProperty("InstallSize", out var bytes) && bytes.ValueKind == JsonValueKind.Number && bytes.TryGetInt64(out var value) && value > 0 ? value : null;
            var incomplete = Flag(root, "bIsIncompleteInstall");
            return new GameEntry(
                appName,
                name.Trim(),
                GamePlatform.Epic,
                string.IsNullOrWhiteSpace(folder) ? null : folder,
                size,
                incomplete ? GameState.Incomplete : GameState.Installed,
                incomplete ? null : GameLaunch.Epic(appName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            GamesLog.FileSkipped(logger, ex, item);
            return null;
        }
    }
}
