using System.Security;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace M1vClock.Optimize.Apps;

public sealed class WindowsInstalledAppSource(ILogger<WindowsInstalledAppSource> logger) : IInstalledAppSource
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public ReadResult<IReadOnlyList<InstalledAppRaw>> Read()
    {
        var all = new List<InstalledAppRaw>();
        var failures = new List<string>();
        Collect(all, failures, RegistryHive.CurrentUser, RegistryView.Registry64, AppScope.User);
        Collect(all, failures, RegistryHive.LocalMachine, RegistryView.Registry64, AppScope.Machine);
        Collect(all, failures, RegistryHive.LocalMachine, RegistryView.Registry32, AppScope.Machine32);
        return all.Count == 0 && failures.Count > 0
            ? ReadResult.Failure<IReadOnlyList<InstalledAppRaw>>(string.Join(' ', failures))
            : ReadResult.Success<IReadOnlyList<InstalledAppRaw>>(all);
    }

    private static InstalledAppRaw Raw(RegistryKey key, string name, AppScope scope) => new(
        name,
        scope,
        Text(key, "DisplayName"),
        Text(key, "DisplayVersion"),
        Text(key, "Publisher"),
        Text(key, "InstallDate"),
        Number(key, "EstimatedSize"),
        Text(key, "UninstallString"),
        (int?)Number(key, "SystemComponent"),
        Text(key, "ParentKeyName"));

    private static string? Text(RegistryKey key, string name) => key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

    private static long? Number(RegistryKey key, string name) => key.GetValue(name) switch
    {
        int value => value,
        long value => value,
        string text when long.TryParse(text, out var parsed) => parsed,
        _ => null,
    };

    private void Collect(List<InstalledAppRaw> all, List<string> failures, RegistryHive hive, RegistryView view, AppScope scope)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(UninstallKey);
            if (uninstall is null)
            {
                return;
            }

            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(name);
                if (key is not null)
                {
                    all.Add(Raw(key, name, scope));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            OptimizeLog.AppsFailed(logger, ex, scope.ToString());
            failures.Add($"{scope}: {ex.Message}");
        }
    }
}
