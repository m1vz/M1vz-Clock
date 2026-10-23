using M1vClock.Core.Results;

namespace M1vClock.Optimize.Apps;

public enum AppScope
{
    User,
    Machine,
    Machine32,
}

public sealed record InstalledAppRaw(
    string KeyName,
    AppScope Scope,
    string? DisplayName,
    string? DisplayVersion,
    string? Publisher,
    string? InstallDate,
    long? EstimatedSizeKilobytes,
    string? UninstallString,
    int? SystemComponent,
    string? ParentKeyName);

public sealed record InstalledApp(
    string Id,
    string Name,
    string? Version,
    string? Publisher,
    DateOnly? InstalledOn,
    long? SizeBytes,
    AppScope Scope,
    string? UninstallCommand)
{
    public UninstallLaunch? Launch { get; init; }
}

public sealed record UninstallLaunch(string FileName, string Arguments);

public sealed record AppReport(IReadOnlyList<InstalledApp> Apps)
{
    public long KnownBytes => Apps.Sum(a => a.SizeBytes ?? 0);

    public InstalledApp? Largest => Apps.Where(a => a.SizeBytes is > 0).OrderByDescending(a => a.SizeBytes).FirstOrDefault();
}

public enum AppSort
{
    Size,
    Name,
    Date,
}

public interface IInstalledAppSource
{
    ReadResult<IReadOnlyList<InstalledAppRaw>> Read();
}
