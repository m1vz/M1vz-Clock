using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Hardware.Storage;

namespace M1vClock.App.Pages.Hardware;

public static class DiskRows
{
    public static string Verdict(DiskVerdict verdict) => Loc.Get($"Hw.Storage.Verdict.{verdict}");

    public static MetricState Tone(DiskVerdict verdict) => verdict switch
    {
        DiskVerdict.Good => MetricState.Good,
        DiskVerdict.Warning => MetricState.Warning,
        DiskVerdict.Critical => MetricState.Critical,
        _ => MetricState.Neutral,
    };

    public static string Finding(DiskFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var key = $"Hw.Storage.Finding.{finding.Kind}";
        return finding.Kind switch
        {
            FindingKind.WearEndOfLife or FindingKind.WearHigh or FindingKind.TemperatureCritical or FindingKind.TemperatureHigh =>
                Loc.Format(key, finding.Value.ToString("0", Loc.Culture)),
            FindingKind.UncorrectedErrors => Loc.Format(key, finding.Value.ToString("N0", Loc.Culture), finding.Second.ToString("N0", Loc.Culture)),
            _ => Loc.Get(key),
        };
    }

    public static IReadOnlyList<string> Notes(DiskAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (assessment.Findings.Count > 0)
        {
            return assessment.Findings.OrderByDescending(f => f.Severity).Select(Finding).ToList();
        }

        return [Loc.Get(assessment.Verdict == DiskVerdict.Unknown ? "Hw.Storage.Finding.NoData" : "Hw.Storage.Finding.None")];
    }

    public static string Subtitle(PhysicalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var kind = disk.Media switch
        {
            DriveMedia.Hdd => "HDD",
            DriveMedia.Ssd => "SSD",
            DriveMedia.Scm => "SCM",
            _ => disk.IsSolidState ? "SSD" : string.Empty,
        };
        var bus = disk.Bus switch
        {
            DriveBus.Sata => "SATA",
            DriveBus.Nvme => "NVMe",
            DriveBus.Usb => "USB",
            DriveBus.Sas => "SAS",
            DriveBus.Raid => "RAID",
            _ => string.Empty,
        };
        var letters = disk.Letters.Count > 0 ? Loc.Format("Hw.Storage.Letters", string.Join(" ", disk.Letters)) : string.Empty;
        var parts = new[] { kind, bus, disk.SizeBytes > 0 ? Units.DecimalBytes(disk.SizeBytes) : string.Empty, letters };
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    public static IReadOnlyList<InfoRowViewModel> Metrics(PhysicalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var rows = new List<InfoRowViewModel>();
        var counters = disk.Counters;
        if (counters?.Temperature is { } temperature)
        {
            var text = counters.TemperatureMax is > 0 and { } max
                ? Loc.Format("Hw.Storage.Metric.Temperature.WithMax", Whole(temperature), Whole(max))
                : Loc.Format("Hw.Storage.Metric.Temperature.Value", Whole(temperature));
            rows.Add(Row("Hw.Storage.Metric.Temperature", text));
        }

        if (counters?.Wear is { } wear)
        {
            rows.Add(Row("Hw.Storage.Metric.Wear", $"{Whole(wear)}%"));
            rows.Add(Row("Hw.Storage.Metric.Life", $"{Whole(Math.Max(0, 100 - wear))}%"));
        }

        if (counters?.PowerOnHours is { } hours)
        {
            rows.Add(Row("Hw.Storage.Metric.Hours", Loc.Format("Hw.Storage.Metric.Hours.Value", hours.ToString("N0", Loc.Culture), (hours / 24.0).ToString("N0", Loc.Culture))));
        }

        if (counters?.StartStopCycles is { } cycles)
        {
            rows.Add(Row("Hw.Storage.Metric.Cycles", cycles.ToString("N0", Loc.Culture)));
        }

        if (counters?.ReadErrors is { } readErrors)
        {
            rows.Add(Row("Hw.Storage.Metric.ReadErrors", WithUncorrected(readErrors, counters.ReadErrorsUncorrected)));
        }

        if (counters?.WriteErrors is { } writeErrors)
        {
            rows.Add(Row("Hw.Storage.Metric.WriteErrors", WithUncorrected(writeErrors, counters.WriteErrorsUncorrected)));
        }

        if (disk.FailurePredicted is { } predicted)
        {
            rows.Add(Row("Hw.Storage.Metric.Predicted", predicted ? Loc.Get("Hw.Storage.Metric.Predicted.Yes") : Loc.Get("Hw.Common.No")));
        }

        if (disk.Health != WindowsHealth.Unknown)
        {
            rows.Add(Row("Hw.Storage.Metric.Health", Loc.Get($"Hw.Storage.Health.{disk.Health}")));
        }

        if (disk.Firmware.Length > 0)
        {
            rows.Add(Row("Hw.Storage.Metric.Firmware", disk.Firmware));
        }

        rows.Add(new InfoRowViewModel(Loc.Get("Hw.Storage.Metric.Serial"), disk.Serial.Display(reveal: true), isSensitive: true));
        return rows;
    }

    private static string WithUncorrected(long total, long? uncorrected) => uncorrected is > 0
        ? Loc.Format("Hw.Storage.Metric.Uncorrected", total.ToString("N0", Loc.Culture), uncorrected.Value.ToString("N0", Loc.Culture))
        : total.ToString("N0", Loc.Culture);

    private static string Whole(double value) => value.ToString("0", Loc.Culture);

    private static InfoRowViewModel Row(string labelKey, string value) => new(Loc.Get(labelKey), value);
}
