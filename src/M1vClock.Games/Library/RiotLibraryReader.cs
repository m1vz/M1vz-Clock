using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Library;

public sealed class RiotLibraryReader(IGameFileSystem files, ILogger logger)
{
    private static readonly Dictionary<string, string> Products = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VALORANT"] = "valorant",
        ["League of Legends"] = "league_of_legends",
        ["Legends of Runeterra"] = "bacon",
    };

    public IReadOnlyList<GameEntry> Read(string installsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installsFile);
        var games = new List<GameEntry>();
        try
        {
            using var document = JsonDocument.Parse(files.ReadAllText(installsFile));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("associated_client", out var clients)
                || clients.ValueKind != JsonValueKind.Object)
            {
                return games;
            }

            foreach (var client in clients.EnumerateObject())
            {
                if (client.Value.ValueKind == JsonValueKind.String && Entry(client.Name, client.Value.GetString()) is { } game)
                {
                    games.Add(game);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            GamesLog.FileSkipped(logger, ex, installsFile);
        }

        return games;
    }

    private GameEntry? Entry(string installPath, string? clientPath)
    {
        if (string.IsNullOrWhiteSpace(clientPath))
        {
            return null;
        }

        var folder = Path.GetFullPath(installPath).TrimEnd('\\', '/');
        var patchline = Path.GetFileName(folder);
        var parent = Path.GetDirectoryName(folder);
        var product = parent is null ? string.Empty : Path.GetFileName(parent);
        if (product.Length == 0 || !files.DirectoryExists(folder))
        {
            return null;
        }

        var client = Path.GetFullPath(clientPath);
        var launch = Products.TryGetValue(product, out var id) && files.FileExists(client) ? GameLaunch.Riot(client, id, patchline) : null;
        return new GameEntry(product + "/" + patchline, product, GamePlatform.Riot, folder, null, GameState.Installed, launch);
    }
}
