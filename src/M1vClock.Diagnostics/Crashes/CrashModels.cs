using M1vClock.Core.Results;

namespace M1vClock.Diagnostics.Crashes;

public enum DumpMode
{
    Disabled,
    Small,
    Kernel,
    Complete,
    Automatic,
    Unknown,
}

public sealed record DumpSettings(DumpMode Mode, string MinidumpDirectory, string DumpFile, bool AutoReboot, bool LogsEvent)
{
    public bool Collects => Mode is not (DumpMode.Disabled or DumpMode.Unknown);
}

public enum ShutdownCause
{
    BlueScreen,
    PowerButton,
    Unknown,
}

public sealed record BugCheckInfo(uint Code, IReadOnlyList<ulong> Parameters)
{
    public string Hex => $"0x{Code:X8}";

    public string? Name => BugCheckCatalog.NameOf(Code);
}

public sealed record UnexpectedShutdown(DateTimeOffset Time, ShutdownCause Cause, BugCheckInfo? BugCheck);

public sealed record AppCrash(DateTimeOffset Time, string App, string Module, string? ModulePath, uint ExceptionCode, string? AppPath);

public enum FaultOrigin
{
    Application,
    Windows,
    GraphicsDriver,
    ThirdPartyModule,
}

public sealed record AppCrashGroup(
    string App,
    string Module,
    uint ExceptionCode,
    FaultOrigin Origin,
    string? Vendor,
    int Count,
    DateTimeOffset First,
    DateTimeOffset Last)
{
    public string ExceptionHex => $"0x{ExceptionCode:X8}";

    public string? ExceptionName => ExceptionCatalog.NameOf(ExceptionCode);
}

public sealed record DumpFileInfo(string Path, string Name, long Length, DateTimeOffset Modified, BugCheckInfo? BugCheck);

public sealed record CrashReport(
    TimeSpan Window,
    ReadResult<DumpSettings> Settings,
    IReadOnlyList<UnexpectedShutdown> Shutdowns,
    IReadOnlyList<AppCrashGroup> AppCrashes,
    IReadOnlyList<DumpFileInfo> KernelDumps,
    IReadOnlyList<DumpFileInfo> UserDumps,
    IReadOnlyDictionary<string, string> Failures)
{
    public int BlueScreens => Shutdowns.Count(s => s.Cause == ShutdownCause.BlueScreen);

    public int AppCrashCount => AppCrashes.Sum(c => c.Count);
}

public interface ICrashSource
{
    ReadResult<DumpSettings> ReadSettings();

    ReadResult<IReadOnlyList<string>> ReadEvents(string log, int id, string provider, TimeSpan window, int limit, CancellationToken cancellationToken);

    ReadResult<IReadOnlyList<DumpFileInfo>> ReadDumps(DumpFolder folder);
}

public enum DumpFolder
{
    Kernel,
    User,
}
