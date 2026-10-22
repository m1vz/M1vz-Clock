using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Diagnostics;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Pages.Performance;
using M1vClock.Benchmarks.Results;
using M1vClock.Core.Results;
using M1vClock.Diagnostics.Crashes;
using M1vClock.Diagnostics.Events;
using M1vClock.Diagnostics.Reliability;
using M1vClock.Hardware.Gpu;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Power;
using M1vClock.Hardware.Ram;
using M1vClock.Hardware.Storage;

namespace M1vClock.App.Services.Reports;

public static class ReportSections
{
    public const int EventsListed = 10;
    public const int ShutdownsListed = 10;
    public const int AppCrashesListed = 5;

    public static ReportSection System(PlatformReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<ReportRow>();
        var notes = new List<string>();
        if (report.Os.TryGet(out var os))
        {
            var computer = Loc.Get("Hw.System.Os.Computer");
            rows.AddRange(Plain(SystemRows.Os(os)).Where(r => r.Label != computer));
        }
        else
        {
            notes.Add(NotRead(report.Os.Error));
        }

        Add(rows, notes, report.Board, SystemRows.Board);
        Add(rows, notes, report.Bios, SystemRows.Bios);
        if (report.Cpus.TryGet(out var cpus))
        {
            rows.AddRange(cpus.SelectMany(c => Plain(SystemRows.Cpu(c))));
        }
        else
        {
            notes.Add(NotRead(report.Cpus.Error));
        }

        return Section(ReportSectionIds.System, rows, notes);
    }

    public static ReportSection Gpu(GpuReport report, GpuSpecDatabase database)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(database);
        var rows = new List<ReportRow>();
        var notes = new List<string>();
        if (report.Gpus.TryGet(out var gpus))
        {
            foreach (var gpu in gpus)
            {
                rows.AddRange(Plain(GpuRows.Identification(gpu, database)));
                rows.AddRange(Plain(GpuRows.Memory(gpu)));
                rows.AddRange(Plain(GpuRows.Driver(gpu)));
            }
        }
        else
        {
            notes.Add(NotRead(report.Gpus.Error));
        }

        return Section(ReportSectionIds.Gpu, rows, notes);
    }

    public static ReportSection Memory(RamReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<ReportRow>();
        var notes = new List<string>();
        if (report.Inventory.TryGet(out var inventory))
        {
            rows.AddRange(Plain(RamRows.Summary(inventory.Summary)));
            foreach (var module in inventory.Modules)
            {
                rows.AddRange(Plain(RamRows.Module(module)).Select(r => r with { Label = $"{RamRows.ModuleTitle(module)} · {r.Label}" }));
            }
        }
        else
        {
            notes.Add(NotRead(report.Inventory.Error));
        }

        Add(rows, notes, report.Usage, RamRows.Usage);
        return Section(ReportSectionIds.Memory, rows, notes);
    }

    public static ReportSection Storage(StorageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<ReportRow>();
        var notes = new List<string>();
        if (report.Disks.TryGet(out var disks))
        {
            foreach (var entry in disks)
            {
                var title = DiskRows.Subtitle(entry.Disk);
                rows.Add(new ReportRow(title, DiskRows.Verdict(entry.Assessment.Verdict)));
                rows.AddRange(Plain(DiskRows.Metrics(entry.Disk)).Select(r => r with { Label = $"{r.Label}" }));
                notes.AddRange(DiskRows.Notes(entry.Assessment));
            }
        }
        else
        {
            notes.Add(NotRead(report.Disks.Error));
        }

        return Section(ReportSectionIds.Storage, rows, notes);
    }

    public static ReportSection Power(PowerReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<ReportRow>();
        var notes = new List<string>();
        var plan = report.Plan.TryGet(out var read) ? read : null;
        rows.AddRange(Plain(PowerRows.Energy(report.Source, plan)));
        if (report.Batteries.TryGet(out var batteries))
        {
            var onMains = report.Source != PowerSource.Battery;
            foreach (var entry in batteries)
            {
                rows.Add(new ReportRow(PowerRows.Title(entry.Battery, batteries.Count), PowerRows.Verdict(entry.Assessment.Verdict)));
                rows.AddRange(Plain(PowerRows.Metrics(entry.Battery, onMains)));
                notes.AddRange(PowerRows.Notes(entry.Assessment));
            }
        }
        else
        {
            notes.Add(NotRead(report.Batteries.Error));
        }

        return Section(ReportSectionIds.Power, rows, notes);
    }

    public static ReportSection Events(EventReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<ReportRow>
        {
            new(Loc.Get("Diag.Events.Summary.Critical"), report.CountOf(EventSeverity.Critical).ToString("N0", Loc.Culture)),
            new(Loc.Get("Diag.Events.Summary.Error"), report.CountOf(EventSeverity.Error).ToString("N0", Loc.Culture)),
            new(Loc.Get("Diag.Events.Summary.Warning"), report.CountOf(EventSeverity.Warning).ToString("N0", Loc.Culture)),
        };
        rows.AddRange(report.Groups
            .Where(g => !EventsPageViewModel.IsNoise(g))
            .Take(EventsListed)
            .Select(g =>
            {
                var row = new EventGroupViewModel(g);
                return new ReportRow($"{row.Title} ({g.Provider} {g.Id})", $"{row.CountText} · {row.LastText}");
            }));
        var notes = report.FailedLogs.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => Loc.Format("Diag.Events.LogFailed", f.Key, f.Value)).ToList();
        return Section(ReportSectionIds.Events, rows, notes);
    }

    public static ReportSection Crashes(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<ReportRow>();
        var notes = new List<string>();
        if (report.Settings.TryGet(out var settings))
        {
            rows.Add(new ReportRow(Loc.Get("Diag.Crashes.Settings.Mode"), Loc.Get($"Diag.Dump.Mode.{settings.Mode}")));
            if (CrashRows.Advice(settings) is { Length: > 0 } advice)
            {
                notes.Add(advice);
            }
        }

        rows.Add(new ReportRow(Loc.Get("Diag.Crashes.Summary.BlueScreens"), report.BlueScreens.ToString("N0", Loc.Culture)));
        rows.Add(new ReportRow(Loc.Get("Diag.Crashes.Summary.Shutdowns"), report.Shutdowns.Count.ToString("N0", Loc.Culture)));
        rows.AddRange(report.Shutdowns.Take(ShutdownsListed).Select(s =>
        {
            var row = CrashRows.Shutdown(s);
            return new ReportRow(row.TrailingTop, row.Title);
        }));
        rows.AddRange(report.AppCrashes.Take(AppCrashesListed).Select(g =>
        {
            var row = CrashRows.App(g);
            return new ReportRow(row.Title, $"{row.TrailingTop} · {row.Meta}");
        }));
        notes.AddRange(report.Failures.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => Loc.Format("Diag.Crashes.Failed", Loc.Get($"Diag.Crashes.Part.{f.Key}"), f.Value)));
        return Section(ReportSectionIds.Crashes, rows, notes);
    }

    public static ReportSection Reliability(ReliabilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = new List<ReportRow>();
        var notes = new List<string>();
        if (report.Stability.IsSuccess)
        {
            rows.Add(new ReportRow(Loc.Get("Diag.Reliability.Index.Current"), $"{ReliabilityRows.Index(report.Current)} · {ReliabilityRows.Verdict(report.Verdict)}"));
            rows.Add(new ReportRow(Loc.Get("Diag.Reliability.Index.Lowest"), ReliabilityRows.Index(report.Lowest)));
        }
        else
        {
            notes.Add(NotRead(report.Stability.Error));
        }

        if (report.Records.IsSuccess)
        {
            rows.Add(new ReportRow(Loc.Get("Diag.Reliability.Summary.Failures"), report.Failures.ToString("N0", Loc.Culture)));
        }
        else
        {
            notes.Add(NotRead(report.Records.Error));
        }

        return Section(ReportSectionIds.Reliability, rows, notes);
    }

    public static ReportSection Benchmarks(IReadOnlyList<BenchmarkRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var rows = new List<ReportRow>();
        foreach (var info in BenchmarkCatalog.All)
        {
            if (records.Where(r => r.Test == info.Id).OrderByDescending(r => r.At).FirstOrDefault() is not { } latest)
            {
                continue;
            }

            rows.Add(new ReportRow(BenchNames.Test(info.Id), $"{BenchNames.Value(latest.Test, latest.Score)} · {latest.At.ToLocalTime().ToString("d", Loc.Culture)}"));
        }

        return Section(ReportSectionIds.Benchmarks, rows, []);
    }

    public static string NotRead(string? error) => Loc.Format("Diag.Report.NotRead", error ?? Loc.Get("Hw.Common.Unknown"));

    private static void Add<T>(List<ReportRow> rows, List<string> notes, ReadResult<T> result, Func<T, IReadOnlyList<InfoRowViewModel>> build)
    {
        if (result.TryGet(out var value))
        {
            rows.AddRange(Plain(build(value)));
        }
        else
        {
            notes.Add(NotRead(result.Error));
        }
    }

    private static IEnumerable<ReportRow> Plain(IEnumerable<InfoRowViewModel> rows) =>
        rows.Where(r => !r.IsSensitive && r.Value != Units.Missing).Select(r => new ReportRow(r.Label, r.Value));

    private static ReportSection Section(string id, List<ReportRow> rows, List<string> notes) =>
        new(id, Loc.Get($"Diag.Report.Section.{id}"), rows, notes);
}
