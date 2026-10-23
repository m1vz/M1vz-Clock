using M1vClock.Core.Results;
using Microsoft.Win32;

namespace M1vClock.Optimize.Startup;

public enum StartupLocation
{
    UserRun,
    MachineRun,
    MachineRun32,
    UserFolder,
    CommonFolder,
}

public enum StartupScope
{
    User,
    Machine,
}

public sealed record StartupRaw(string Name, StartupLocation Location, string Command, byte[]? Approval);

public sealed record FileDetails(bool Exists, string? Description, string? Company);

public sealed record StartupEntry(
    string Name,
    StartupLocation Location,
    string Command,
    string? ExecutablePath,
    bool Enabled,
    bool TargetMissing,
    string? Description,
    string? Company)
{
    public StartupScope Scope => Location is StartupLocation.UserRun or StartupLocation.UserFolder ? StartupScope.User : StartupScope.Machine;

    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : Description;
}

public sealed record StartupReport(IReadOnlyList<StartupEntry> Entries)
{
    public int Enabled => Entries.Count(e => e.Enabled);

    public int Disabled => Entries.Count(e => !e.Enabled);

    public int Missing => Entries.Count(e => e.TargetMissing);
}

public interface IStartupSource
{
    ReadResult<IReadOnlyList<StartupRaw>> Read();
}

public interface IFileDetailsSource
{
    FileDetails Describe(string path);
}

public static class StartupApproval
{
    public const int Length = 12;

    public static bool IsEnabled(byte[]? approval) => approval is not { Length: > 0 } || approval[0] is 2 or 6;

    public static byte[] Enabled() => [2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public static byte[] Disabled(DateTimeOffset at)
    {
        var bytes = new byte[Length];
        bytes[0] = 3;
        BitConverter.TryWriteBytes(bytes.AsSpan(4), at.ToFileTime());
        return bytes;
    }

    public static (RegistryHive Hive, string KeyPath) KeyOf(StartupLocation location) => location switch
    {
        StartupLocation.UserRun => (RegistryHive.CurrentUser, Key("Run")),
        StartupLocation.MachineRun => (RegistryHive.LocalMachine, Key("Run")),
        StartupLocation.MachineRun32 => (RegistryHive.LocalMachine, Key("Run32")),
        StartupLocation.UserFolder => (RegistryHive.CurrentUser, Key("StartupFolder")),
        StartupLocation.CommonFolder => (RegistryHive.LocalMachine, Key("StartupFolder")),
        _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
    };

    private static string Key(string leaf) => $@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{leaf}";
}
