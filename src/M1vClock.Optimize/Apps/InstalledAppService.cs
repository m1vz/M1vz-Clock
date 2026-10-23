using System.Globalization;
using M1vClock.Core.Results;
using M1vClock.Optimize.Startup;

namespace M1vClock.Optimize.Apps;

public interface IInstalledAppService
{
    Task<ReadResult<AppReport>> ReadAsync(CancellationToken cancellationToken);
}

public sealed class InstalledAppService : IInstalledAppService
{
    private readonly IInstalledAppSource _source;
    private readonly Func<string, bool> _exists;

    public InstalledAppService(IInstalledAppSource source)
        : this(source, File.Exists)
    {
    }

    public InstalledAppService(IInstalledAppSource source, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(exists);
        _source = source;
        _exists = exists;
    }

    public Task<ReadResult<AppReport>> ReadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => _source.Read().Map(raw => new AppReport(Describe(raw).Select(app => app with { Launch = ParseUninstall(app.UninstallCommand, _exists) }).ToList())), cancellationToken);

    public static UninstallLaunch? ParseUninstall(string? command, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        var executable = CommandLineParser.ExecutableOf(expanded);
        if (executable is null)
        {
            return null;
        }

        var resolved = executable.Contains('\\', StringComparison.Ordinal) || executable.Contains('/', StringComparison.Ordinal)
            ? executable
            : ResolveInSystem(executable, exists);
        if (resolved is null || !exists(resolved))
        {
            return null;
        }

        var start = expanded.IndexOf(executable, StringComparison.OrdinalIgnoreCase) + executable.Length;
        var arguments = expanded[start..].TrimStart('"').Trim();
        return new UninstallLaunch(resolved, arguments);
    }

    public static IReadOnlyList<InstalledApp> Sort(IReadOnlyList<InstalledApp> apps, AppSort sort)
    {
        ArgumentNullException.ThrowIfNull(apps);
        return sort switch
        {
            AppSort.Name => apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            AppSort.Date => apps.OrderByDescending(a => a.InstalledOn ?? DateOnly.MinValue).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ => apps.OrderByDescending(a => a.SizeBytes ?? -1).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        };
    }

    public static IReadOnlyList<InstalledApp> Describe(IReadOnlyList<InstalledAppRaw> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return raw
            .Where(r => !string.IsNullOrWhiteSpace(r.DisplayName) && r.SystemComponent != 1 && string.IsNullOrWhiteSpace(r.ParentKeyName))
            .Select(r => new InstalledApp(
                r.KeyName,
                r.DisplayName!.Trim(),
                string.IsNullOrWhiteSpace(r.DisplayVersion) ? null : r.DisplayVersion.Trim(),
                string.IsNullOrWhiteSpace(r.Publisher) ? null : r.Publisher.Trim(),
                DateOf(r.InstallDate),
                r.EstimatedSizeKilobytes is > 0 ? r.EstimatedSizeKilobytes * 1024 : null,
                r.Scope,
                string.IsNullOrWhiteSpace(r.UninstallString) ? null : r.UninstallString.Trim()))
            .GroupBy(a => (a.Name.ToUpperInvariant(), a.Version))
            .Select(g => g.OrderBy(a => a.Scope).First())
            .ToList();
    }

    private static DateOnly? DateOf(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string? ResolveInSystem(string name, Func<string, bool> exists)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var file = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        var candidate = Path.Combine(system, file);
        return exists(candidate) ? candidate : null;
    }
}
