using M1vClock.Core.Results;
using M1vClock.Diagnostics.Events;
using Microsoft.Extensions.Logging;

namespace M1vClock.Diagnostics.Crashes;

public interface ICrashService
{
    Task<CrashReport> ReadAsync(TimeSpan window, CancellationToken cancellationToken);
}

public sealed class CrashService(ICrashSource source, ILogger<CrashService> logger) : ICrashService
{
    public const int EventsPerKind = 2000;

    public const string SettingsPart = "settings";
    public const string ShutdownsPart = "shutdowns";
    public const string AppCrashesPart = "apps";
    public const string KernelDumpsPart = "kernelDumps";
    public const string UserDumpsPart = "userDumps";

    public async Task<CrashReport> ReadAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        if (window <= TimeSpan.Zero || window > EventService.MaximumWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "A janela vai de zero a 90 dias.");
        }

        var settings = Task.Run(source.ReadSettings, cancellationToken);
        var shutdowns = Task.Run(() => source.ReadEvents("System", 41, "Microsoft-Windows-Kernel-Power", window, EventsPerKind, cancellationToken), cancellationToken);
        var apps = Task.Run(() => source.ReadEvents("Application", 1000, "Application Error", window, EventsPerKind, cancellationToken), cancellationToken);
        var kernel = Task.Run(() => source.ReadDumps(DumpFolder.Kernel), cancellationToken);
        var user = Task.Run(() => source.ReadDumps(DumpFolder.User), cancellationToken);
        await Task.WhenAll(settings, shutdowns, apps, kernel, user).ConfigureAwait(false);

        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        var shutdownList = Collect(shutdowns.Result, ShutdownsPart, failures, xml => CrashParsers.ParseKernelPower(xml, logger))
            .OrderByDescending(s => s.Time)
            .ToList();
        var crashList = Collect(apps.Result, AppCrashesPart, failures, xml => CrashParsers.ParseAppCrash(xml, logger));
        return new CrashReport(
            window,
            settings.Result,
            shutdownList,
            Group(crashList),
            Dumps(kernel.Result, KernelDumpsPart, failures),
            Dumps(user.Result, UserDumpsPart, failures),
            failures);
    }

    public static IReadOnlyList<AppCrashGroup> Group(IEnumerable<AppCrash> crashes)
    {
        ArgumentNullException.ThrowIfNull(crashes);
        return crashes
            .GroupBy(c => (App: c.App.ToUpperInvariant(), Module: c.Module.ToUpperInvariant(), c.ExceptionCode))
            .Select(g =>
            {
                var ordered = g.OrderBy(c => c.Time).ToList();
                var newest = ordered[^1];
                var (origin, vendor) = FaultClassifier.Classify(newest);
                return new AppCrashGroup(newest.App, newest.Module, newest.ExceptionCode, origin, vendor, ordered.Count, ordered[0].Time, newest.Time);
            })
            .OrderByDescending(g => g.Last)
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.App, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<T> Collect<T>(ReadResult<IReadOnlyList<string>> result, string part, Dictionary<string, string> failures, Func<string, T?> parse)
        where T : class
    {
        if (!result.TryGet(out var xmls))
        {
            failures[part] = result.Error ?? "Leitura sem resposta.";
            return [];
        }

        return xmls.Select(parse).OfType<T>().ToList();
    }

    private static List<DumpFileInfo> Dumps(ReadResult<IReadOnlyList<DumpFileInfo>> result, string part, Dictionary<string, string> failures)
    {
        if (result.TryGet(out var files))
        {
            return files.OrderByDescending(f => f.Modified).ToList();
        }

        failures[part] = result.Error ?? "Leitura sem resposta.";
        return [];
    }
}
