namespace M1vClock.Optimize.Cleanup;

public static class CleanupCatalog
{
    public const string UserTemp = "userTemp";
    public const string WindowsTemp = "windowsTemp";
    public const string UpdateDownloads = "updateDownloads";
    public const string CrashDumps = "crashDumps";
    public const string ErrorReports = "errorReports";
    public const string ShaderCache = "shaderCache";

    public static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    public static IReadOnlyList<string> Ids { get; } = [UserTemp, WindowsTemp, UpdateDownloads, CrashDumps, ErrorReports, ShaderCache];

    public static IReadOnlyList<CleanupCategory> Create(CleanupPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return
        [
            new CleanupCategory(UserTemp, [paths.Temp], false, OneDay, false),
            new CleanupCategory(WindowsTemp, [Path.Combine(paths.WindowsDirectory, "Temp")], true, OneDay, false),
            new CleanupCategory(UpdateDownloads, [Path.Combine(paths.WindowsDirectory, "SoftwareDistribution", "Download")], true, OneDay, true),
            new CleanupCategory(CrashDumps, [Path.Combine(paths.LocalAppData, "CrashDumps")], false, TimeSpan.Zero, false),
            new CleanupCategory(
                ErrorReports,
                [Path.Combine(paths.ProgramData, "Microsoft", "Windows", "WER", "ReportArchive"), Path.Combine(paths.ProgramData, "Microsoft", "Windows", "WER", "ReportQueue")],
                true,
                OneDay,
                false),
            new CleanupCategory(ShaderCache, [Path.Combine(paths.LocalAppData, "D3DSCache")], false, TimeSpan.Zero, true),
        ];
    }
}
