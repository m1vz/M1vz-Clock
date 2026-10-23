using System.Diagnostics;
using System.Security;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace M1vClock.Optimize.Startup;

public sealed class WindowsStartupSource(ILogger<WindowsStartupSource> logger) : IStartupSource
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public ReadResult<IReadOnlyList<StartupRaw>> Read()
    {
        var all = new List<StartupRaw>();
        var failures = new List<string>();
        Collect(all, failures, nameof(StartupLocation.UserRun), () => FromRun(RegistryHive.CurrentUser, RegistryView.Registry64, StartupLocation.UserRun));
        Collect(all, failures, nameof(StartupLocation.MachineRun), () => FromRun(RegistryHive.LocalMachine, RegistryView.Registry64, StartupLocation.MachineRun));
        Collect(all, failures, nameof(StartupLocation.MachineRun32), () => FromRun(RegistryHive.LocalMachine, RegistryView.Registry32, StartupLocation.MachineRun32));
        Collect(all, failures, nameof(StartupLocation.UserFolder), () => FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLocation.UserFolder));
        Collect(all, failures, nameof(StartupLocation.CommonFolder), () => FromFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupLocation.CommonFolder));
        return all.Count == 0 && failures.Count > 0
            ? ReadResult.Failure<IReadOnlyList<StartupRaw>>(string.Join(' ', failures))
            : ReadResult.Success<IReadOnlyList<StartupRaw>>(all);
    }

    private static List<StartupRaw> FromRun(RegistryHive hive, RegistryView view, StartupLocation location)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var run = root.OpenSubKey(RunKey);
        if (run is null)
        {
            return [];
        }

        var (approvalHive, approvalKey) = StartupApproval.KeyOf(location);
        using var approvalRoot = RegistryKey.OpenBaseKey(approvalHive, RegistryView.Registry64);
        using var approvals = approvalRoot.OpenSubKey(approvalKey);
        var list = new List<StartupRaw>();
        foreach (var name in run.GetValueNames())
        {
            if (name.Length == 0 || run.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string command)
            {
                continue;
            }

            list.Add(new StartupRaw(name, location, command, approvals?.GetValue(name) as byte[]));
        }

        return list;
    }

    private static List<StartupRaw> FromFolder(string folder, StartupLocation location)
    {
        var info = new DirectoryInfo(folder);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return [];
        }

        var (approvalHive, approvalKey) = StartupApproval.KeyOf(location);
        using var approvalRoot = RegistryKey.OpenBaseKey(approvalHive, RegistryView.Registry64);
        using var approvals = approvalRoot.OpenSubKey(approvalKey);
        return info.EnumerateFiles()
            .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden) && !f.Attributes.HasFlag(FileAttributes.System) && !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Select(f => new StartupRaw(f.Name, location, f.FullName, approvals?.GetValue(f.Name) as byte[]))
            .ToList();
    }

    private void Collect(List<StartupRaw> all, List<string> failures, string source, Func<List<StartupRaw>> read)
    {
        try
        {
            all.AddRange(read());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            OptimizeLog.StartupSourceFailed(logger, ex, source);
            failures.Add($"{source}: {ex.Message}");
        }
    }
}

public sealed class WindowsFileDetailsSource(ILogger<WindowsFileDetailsSource> logger) : IFileDetailsSource
{
    public FileDetails Describe(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            if (!File.Exists(path))
            {
                return new FileDetails(false, null, null);
            }

            var info = FileVersionInfo.GetVersionInfo(path);
            return new FileDetails(true, Clean(info.FileDescription), Clean(info.CompanyName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            OptimizeLog.FileDetailsFailed(logger, ex, path);
            return new FileDetails(false, null, null);
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
