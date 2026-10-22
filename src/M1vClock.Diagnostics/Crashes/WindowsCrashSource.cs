using System.Diagnostics.Eventing.Reader;
using M1vClock.Core.RegistryValues;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace M1vClock.Diagnostics.Crashes;

public sealed record CrashPaths(string SystemRoot, string LocalAppData)
{
    public static CrashPaths Current { get; } = new(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public string UserDumps => Path.Combine(LocalAppData, "CrashDumps");

    public string DefaultKernelDumps => Path.Combine(SystemRoot, "Minidump");

    public string DefaultFullDump => Path.Combine(SystemRoot, "MEMORY.DMP");
}

public static class DumpSettingsReader
{
    public static readonly RegistryValueAddress Mode = Address("CrashDumpEnabled");
    public static readonly RegistryValueAddress Directory = Address("MinidumpDir");
    public static readonly RegistryValueAddress File = Address("DumpFile");
    public static readonly RegistryValueAddress AutoReboot = Address("AutoReboot");
    public static readonly RegistryValueAddress LogEvent = Address("LogEvent");

    public static DumpSettings Read(IRegistryAccess registry, CrashPaths paths)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(paths);
        return new DumpSettings(
            ModeOf(Number(registry, Mode)),
            Expand(Text(registry, Directory)) ?? paths.DefaultKernelDumps,
            Expand(Text(registry, File)) ?? paths.DefaultFullDump,
            Number(registry, AutoReboot) is > 0,
            Number(registry, LogEvent) is > 0);
    }

    public static DumpMode ModeOf(int? value) => value switch
    {
        0 => DumpMode.Disabled,
        1 => DumpMode.Complete,
        2 => DumpMode.Kernel,
        3 => DumpMode.Small,
        7 => DumpMode.Automatic,
        _ => DumpMode.Unknown,
    };

    private static RegistryValueAddress Address(string name) =>
        new(RegistryHive.LocalMachine, RegistryView.Registry64, @"SYSTEM\CurrentControlSet\Control\CrashControl", name);

    private static int? Number(IRegistryAccess registry, RegistryValueAddress address)
    {
        var state = registry.Read(address);
        return state is { ValueExists: true, Kind: RegistryValueKind.DWord, Data: int value } ? value : null;
    }

    private static string? Text(IRegistryAccess registry, RegistryValueAddress address)
    {
        var state = registry.Read(address);
        return state is { ValueExists: true, Kind: RegistryValueKind.String or RegistryValueKind.ExpandString, Data: string value } && value.Length > 0 ? value : null;
    }

    private static string? Expand(string? value) => value is null ? null : Environment.ExpandEnvironmentVariables(value);
}

public sealed class WindowsCrashSource(IRegistryAccess registry, CrashPaths paths, ILogger<WindowsCrashSource> logger) : ICrashSource
{
    public ReadResult<DumpSettings> ReadSettings()
    {
        try
        {
            return ReadResult.Success(DumpSettingsReader.Read(registry, paths));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            DiagnosticsLog.CrashSettingsFailed(logger, ex);
            return ReadResult.Failure<DumpSettings>(ex.Message);
        }
    }

    public ReadResult<IReadOnlyList<string>> ReadEvents(string log, int id, string provider, TimeSpan window, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        if (provider.Contains('\'', StringComparison.Ordinal))
        {
            throw new ArgumentException("O nome da origem não pode ter apóstrofo.", nameof(provider));
        }

        var query = new EventLogQuery(log, PathType.LogName, $"*[System[Provider[@Name='{provider}'] and (EventID={id}) and TimeCreated[timediff(@SystemTime) <= {(long)window.TotalMilliseconds}]]]")
        {
            ReverseDirection = true,
        };
        try
        {
            using var reader = new EventLogReader(query);
            var xml = new List<string>();
            while (xml.Count < limit && reader.ReadEvent() is { } record)
            {
                using (record)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    xml.Add(record.ToXml());
                }
            }

            return ReadResult.Success<IReadOnlyList<string>>(xml);
        }
        catch (UnauthorizedAccessException)
        {
            DiagnosticsLog.EventLogDenied(logger, log);
            return ReadResult.Failure<IReadOnlyList<string>>($"Sem permissão para ler o log {log}.");
        }
        catch (EventLogException ex)
        {
            DiagnosticsLog.EventLogFailed(logger, ex, log);
            return ReadResult.Failure<IReadOnlyList<string>>(ex.Message);
        }
    }

    public ReadResult<IReadOnlyList<DumpFileInfo>> ReadDumps(DumpFolder folder)
    {
        try
        {
            IReadOnlyList<DumpFileInfo> files = folder == DumpFolder.Kernel ? KernelDumps() : UserDumps();
            return ReadResult.Success(files);
        }
        catch (UnauthorizedAccessException)
        {
            DiagnosticsLog.DumpFolderDenied(logger, folder);
            return ReadResult.Failure<IReadOnlyList<DumpFileInfo>>("Sem permissão para ler a pasta. Abra o m1vClock como administrador.");
        }
        catch (IOException ex)
        {
            DiagnosticsLog.DumpFolderFailed(logger, ex, folder);
            return ReadResult.Failure<IReadOnlyList<DumpFileInfo>>(ex.Message);
        }
    }

    private static DumpFileInfo Describe(FileInfo file, bool readHeader)
    {
        BugCheckInfo? bugCheck = null;
        if (readHeader)
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                bugCheck = DumpHeaderReader.Read(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                bugCheck = null;
            }
        }

        return new DumpFileInfo(file.FullName, file.Name, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), bugCheck);
    }

    private static List<DumpFileInfo> Files(string directory, bool readHeader)
    {
        var info = new DirectoryInfo(directory);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return [];
        }

        return info.EnumerateFiles("*.dmp", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Select(f => Describe(f, readHeader))
            .ToList();
    }

    private List<DumpFileInfo> KernelDumps()
    {
        var settings = DumpSettingsReader.Read(registry, paths);
        var files = Files(settings.MinidumpDirectory, readHeader: true);
        var full = new FileInfo(settings.DumpFile);
        if (full.Exists && !full.Attributes.HasFlag(FileAttributes.ReparsePoint) && !files.Any(f => string.Equals(f.Path, full.FullName, StringComparison.OrdinalIgnoreCase)))
        {
            files.Add(Describe(full, readHeader: true));
        }

        return files;
    }

    private List<DumpFileInfo> UserDumps() => Files(paths.UserDumps, readHeader: false);
}
