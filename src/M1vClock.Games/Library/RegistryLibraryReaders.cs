using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace M1vClock.Games.Library;

public interface IGameRegistry
{
    IReadOnlyList<string> SubKeyNames(string machineKey);

    string? Text(string machineKey, string valueName);
}

public sealed class WindowsGameRegistry : IGameRegistry
{
    public IReadOnlyList<string> SubKeyNames(string machineKey)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = root.OpenSubKey(machineKey, writable: false);
        return key?.GetSubKeyNames() ?? [];
    }

    public string? Text(string machineKey, string valueName)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var key = root.OpenSubKey(machineKey, writable: false);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
}

public sealed class GogLibraryReader(IGameRegistry registry, IGameFileSystem files, ILogger logger)
{
    public const string GamesKey = @"SOFTWARE\GOG.com\Games";

    public IReadOnlyList<GameEntry> Read()
    {
        var games = new List<GameEntry>();
        foreach (var id in registry.SubKeyNames(GamesKey))
        {
            var key = GamesKey + "\\" + id;
            try
            {
                if (Entry(id, key) is { } game)
                {
                    games.Add(game);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
            {
                GamesLog.FileSkipped(logger, ex, key);
            }
        }

        return games;
    }

    private GameEntry? Entry(string id, string key)
    {
        var name = registry.Text(key, "gameName");
        var folder = registry.Text(key, "path");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            return null;
        }

        folder = Path.GetFullPath(folder).TrimEnd('\\', '/');
        if (!files.DirectoryExists(folder))
        {
            return null;
        }

        var exe = registry.Text(key, "exe");
        var launch = exe is not null && GameLaunch.IsExecutablePath(exe) && files.FileExists(exe)
            ? GameLaunch.Executable(exe, registry.Text(key, "launchParam"))
            : null;
        return new GameEntry("gog:" + id, name.Trim(), GamePlatform.Gog, folder, null, GameState.Installed, launch);
    }
}

public sealed class UbisoftLibraryReader(IGameRegistry registry, IGameFileSystem files, ILogger logger)
{
    public const string InstallsKey = @"SOFTWARE\Ubisoft\Launcher\Installs";
    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install ";

    public IReadOnlyList<GameEntry> Read()
    {
        var games = new List<GameEntry>();
        foreach (var id in registry.SubKeyNames(InstallsKey))
        {
            var key = InstallsKey + "\\" + id;
            try
            {
                if (Entry(id, key) is { } game)
                {
                    games.Add(game);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
            {
                GamesLog.FileSkipped(logger, ex, key);
            }
        }

        return games;
    }

    private GameEntry? Entry(string id, string key)
    {
        var folder = registry.Text(key, "InstallDir")?.Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            return null;
        }

        folder = Path.GetFullPath(folder).TrimEnd('\\');
        if (!files.DirectoryExists(folder))
        {
            return null;
        }

        var name = registry.Text(UninstallKey + id, "DisplayName");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileName(folder);
        }

        return new GameEntry("ubisoft:" + id, name.Trim(), GamePlatform.Ubisoft, folder, null, GameState.Installed, GameLaunch.Ubisoft(id));
    }
}
