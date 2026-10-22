namespace M1vClock.Hardware.Storage;

public static class DiskHealthEvaluator
{
    public const double WearCritical = 95;
    public const double WearWarning = 80;
    public const double SsdTemperatureWarning = 70;
    public const double SsdTemperatureCritical = 85;
    public const double HddTemperatureWarning = 55;
    public const double HddTemperatureCritical = 65;

    public static DiskAssessment Evaluate(PhysicalDisk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);
        var findings = new List<DiskFinding>();
        var counters = disk.Counters;

        if (disk.FailurePredicted == true)
        {
            findings.Add(new DiskFinding(FindingKind.FailurePredicted, FindingSeverity.Critical));
        }

        switch (disk.Health)
        {
            case WindowsHealth.Unhealthy:
                findings.Add(new DiskFinding(FindingKind.WindowsUnhealthy, FindingSeverity.Critical));
                break;
            case WindowsHealth.Warning:
                findings.Add(new DiskFinding(FindingKind.WindowsWarning, FindingSeverity.Warning));
                break;
        }

        if (counters?.Wear is { } wear)
        {
            if (wear >= WearCritical)
            {
                findings.Add(new DiskFinding(FindingKind.WearEndOfLife, FindingSeverity.Critical, wear));
            }
            else if (wear >= WearWarning)
            {
                findings.Add(new DiskFinding(FindingKind.WearHigh, FindingSeverity.Warning, wear));
            }
        }

        if (counters?.Temperature is { } temperature)
        {
            var warning = disk.IsSolidState ? SsdTemperatureWarning : HddTemperatureWarning;
            var critical = disk.IsSolidState ? SsdTemperatureCritical : HddTemperatureCritical;
            if (temperature >= critical)
            {
                findings.Add(new DiskFinding(FindingKind.TemperatureCritical, FindingSeverity.Critical, temperature));
            }
            else if (temperature >= warning)
            {
                findings.Add(new DiskFinding(FindingKind.TemperatureHigh, FindingSeverity.Warning, temperature));
            }
        }

        if (counters is { ReadErrorsUncorrected: > 0 } or { WriteErrorsUncorrected: > 0 })
        {
            findings.Add(new DiskFinding(FindingKind.UncorrectedErrors, FindingSeverity.Warning, counters.ReadErrorsUncorrected ?? 0, counters.WriteErrorsUncorrected ?? 0));
        }

        return new DiskAssessment(VerdictFor(disk, findings), findings);
    }

    private static DiskVerdict VerdictFor(PhysicalDisk disk, List<DiskFinding> findings)
    {
        if (findings.Any(f => f.Severity == FindingSeverity.Critical))
        {
            return DiskVerdict.Critical;
        }

        if (findings.Count > 0)
        {
            return DiskVerdict.Warning;
        }

        return HasAnyData(disk) ? DiskVerdict.Good : DiskVerdict.Unknown;
    }

    private static bool HasAnyData(PhysicalDisk disk) =>
        disk.Health != WindowsHealth.Unknown
        || disk.FailurePredicted is not null
        || disk.Counters?.Wear is not null
        || disk.Counters?.Temperature is not null
        || disk.Counters?.PowerOnHours is not null;
}
