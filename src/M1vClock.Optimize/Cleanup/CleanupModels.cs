namespace M1vClock.Optimize.Cleanup;

public sealed record CleanupPaths(string Temp, string WindowsDirectory, string LocalAppData, string ProgramData)
{
    public static CleanupPaths Current { get; } = new(
        Path.GetTempPath().TrimEnd('\\'),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
}

public sealed record CleanupCategory(string Id, IReadOnlyList<string> Roots, bool NeedsAdministrator, TimeSpan MinimumAge, bool RebuildsByItself);

public sealed record CleanupScan(CleanupCategory Category, int Files, long Bytes, int Unreadable);

public sealed record CleanupOutcome(CleanupCategory Category, int Deleted, long FreedBytes, int Failed);

public sealed record CleanupSummary(IReadOnlyList<CleanupScan> Scans)
{
    public long Bytes => Scans.Sum(s => s.Bytes);

    public int Files => Scans.Sum(s => s.Files);
}

public interface ICleanupService
{
    IReadOnlyList<CleanupCategory> Categories { get; }

    Task<CleanupSummary> ScanAsync(CancellationToken cancellationToken);

    Task<CleanupOutcome> CleanAsync(string categoryId, CancellationToken cancellationToken);
}
