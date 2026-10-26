using M1vClock.Core.IO;
using M1vClock.Games.Library;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Profiles;

public enum SteamOptionsResult
{
    Written,
    Unchanged,
    SteamRunning,
    NoConfig,
    UnsafeOptions,
    UnsupportedFormat,
    Failed,
}

public interface ISteamLaunchOptions
{
    string? Read(string appId);

    SteamOptionsResult Write(string appId, string? options);
}

public sealed class SteamLaunchOptions(IGameLocations locations, IGameProcesses processes, ILogger<SteamLaunchOptions> logger) : ISteamLaunchOptions
{
    public const string BackupSuffix = ".m1vclock-original";
    private static readonly string[] AppsPath = ["Software", "Valve", "Steam", "apps"];

    public string? ConfigFile()
    {
        if (locations.SteamFolder is not { } steam)
        {
            return null;
        }

        var userData = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userData))
        {
            return null;
        }

        return Directory.EnumerateDirectories(userData)
            .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public bool SteamIsRunning()
    {
        if (locations.SteamFolder is not { } steam)
        {
            return false;
        }

        var exe = Path.Combine(steam, "steam.exe");
        return processes.List().Any(p => string.Equals(p.Path, exe, StringComparison.OrdinalIgnoreCase));
    }

    public string? Read(string appId)
    {
        if (!GameLaunchIds.IsSteamAppId(appId) || ConfigFile() is not { } file)
        {
            return null;
        }

        try
        {
            return Get(Vdf.Parse(File.ReadAllText(file)), appId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            ProfileLog.SteamWriteFailed(logger, ex, file);
            return null;
        }
    }

    public SteamOptionsResult Write(string appId, string? options)
    {
        if (!GameLaunchIds.IsSteamAppId(appId) || !GameLaunch.IsSafeArguments(options))
        {
            return SteamOptionsResult.UnsafeOptions;
        }

        if (ConfigFile() is not { } file)
        {
            return SteamOptionsResult.NoConfig;
        }

        if (SteamIsRunning())
        {
            return SteamOptionsResult.SteamRunning;
        }

        return WriteFile(file, appId, options, logger);
    }

    public static string? Get(VdfNode root, string appId)
    {
        ArgumentNullException.ThrowIfNull(root);
        VdfNode? node = root.Name.Equals("UserLocalConfigStore", StringComparison.OrdinalIgnoreCase) ? root : root.Child("UserLocalConfigStore");
        foreach (var name in AppsPath)
        {
            node = node?.Child(name);
        }

        return node?.Child(appId)?.Text("LaunchOptions");
    }

    public static string Apply(string original, string appId, string? options)
    {
        ArgumentNullException.ThrowIfNull(original);
        var root = Vdf.Parse(original);
        if (!string.Equals(Vdf.Serialize(root), Normalize(original), StringComparison.Ordinal))
        {
            throw new FormatException("O arquivo não volta igual depois de lido; não é seguro regravar.");
        }

        if (!root.Name.Equals("UserLocalConfigStore", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("O arquivo não é um localconfig.vdf da Steam.");
        }

        var node = root;
        foreach (var name in AppsPath)
        {
            node = node.GetOrAddObject(name);
        }

        var app = node.GetOrAddObject(appId);
        var text = (options ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            app.Remove("LaunchOptions");
        }
        else
        {
            app.Set("LaunchOptions", text);
        }

        return Vdf.Serialize(root);
    }

    internal static SteamOptionsResult WriteFile(string file, string appId, string? options, ILogger logger)
    {
        try
        {
            var original = File.ReadAllText(file);
            var updated = Apply(original, appId, options);
            if (string.Equals(updated, Normalize(original), StringComparison.Ordinal))
            {
                return SteamOptionsResult.Unchanged;
            }

            var backup = file + BackupSuffix;
            if (!File.Exists(backup))
            {
                File.Copy(file, backup);
            }

            AtomicFile.WriteAllText(file, updated);
            var check = Get(Vdf.Parse(File.ReadAllText(file)), appId);
            var expected = string.IsNullOrWhiteSpace(options) ? null : options.Trim();
            if (!string.Equals(check, expected, StringComparison.Ordinal))
            {
                throw new IOException("O arquivo gravado não tem o valor esperado.");
            }

            ProfileLog.SteamWritten(logger, appId, backup);
            return SteamOptionsResult.Written;
        }
        catch (FormatException ex)
        {
            ProfileLog.SteamWriteFailed(logger, ex, file);
            return SteamOptionsResult.UnsupportedFormat;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProfileLog.SteamWriteFailed(logger, ex, file);
            return SteamOptionsResult.Failed;
        }
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('﻿');
}

public static class GameLaunchIds
{
    public static bool IsSteamAppId(string? appId) =>
        appId is { Length: > 0 and <= 12 } && appId.All(char.IsAsciiDigit);
}
