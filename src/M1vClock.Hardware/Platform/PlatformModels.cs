using M1vClock.Core.Privacy;
using M1vClock.Core.Results;

namespace M1vClock.Hardware.Platform;

public sealed record OsInfo(
    string Name,
    string Version,
    string Build,
    string? DisplayVersion,
    string? Revision,
    string Architecture,
    string ComputerName)
{
    public string FullBuild => Revision is null ? Build : $"{Build}.{Revision}";
}

public sealed record BoardInfo(string Manufacturer, string Model, string? Version, SensitiveText SerialNumber);

public sealed record BiosInfo(string Manufacturer, string Version, DateOnly? ReleaseDate);

public sealed record CpuInfo(
    string Name,
    string Manufacturer,
    string? Socket,
    int Cores,
    int Threads,
    int MaxClockMhz,
    int L2CacheKb,
    int L3CacheKb,
    bool? VirtualizationEnabled);

public sealed record PlatformReport(
    ReadResult<OsInfo> Os,
    ReadResult<BoardInfo> Board,
    ReadResult<BiosInfo> Bios,
    ReadResult<IReadOnlyList<CpuInfo>> Cpus);
