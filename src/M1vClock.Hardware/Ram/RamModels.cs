using M1vClock.Core.Privacy;
using M1vClock.Core.Results;

namespace M1vClock.Hardware.Ram;

public enum RamType
{
    Unknown,
    Ddr,
    Ddr2,
    Ddr3,
    Ddr4,
    Ddr5,
    LpDdr5,
}

public enum RamFormFactor
{
    Unknown,
    Dimm,
    SoDimm,
    MicroDimm,
    FbDimm,
    Rimm,
    Srimm,
    Proprietary,
    Other,
}

public sealed record RamModule(
    string Bank,
    string Locator,
    ulong CapacityBytes,
    RamType Type,
    RamFormFactor FormFactor,
    int RatedSpeed,
    int ConfiguredSpeed,
    string Manufacturer,
    string PartNumber,
    SensitiveText SerialNumber,
    int DataWidth,
    int TotalWidth,
    int Ranks,
    double? ConfiguredVolts,
    double? MinVolts,
    double? MaxVolts)
{
    public bool HasEcc => DataWidth > 0 && TotalWidth > DataWidth;
}

public sealed record RamBoard(int Slots, ulong MaxCapacityBytes);

public sealed record RamUsage(ulong TotalBytes, ulong FreeBytes, ulong VirtualTotalBytes, ulong VirtualFreeBytes)
{
    public ulong UsedBytes => TotalBytes > FreeBytes ? TotalBytes - FreeBytes : 0;

    public double UsedFraction => TotalBytes == 0 ? 0 : (double)UsedBytes / TotalBytes;

    public ulong VirtualUsedBytes => VirtualTotalBytes > VirtualFreeBytes ? VirtualTotalBytes - VirtualFreeBytes : 0;
}

public enum ProfileVerdict
{
    SpeedUnreadable,
    ProbablyOff,
    ProbablyOn,
    BelowRated,
    CannotEstimate,
}

public sealed record ProfileEstimate(ProfileVerdict Verdict, int ConfiguredSpeed, int LabelSpeed, int RatedSpeed);

public enum ChannelVerdict
{
    SingleModule,
    MultiChannel,
    SameChannel,
    Unidentified,
}

public sealed record ChannelEstimate(ChannelVerdict Verdict, int Channels, IReadOnlyList<string> Locators);

public sealed record RamSummary(
    int Installed,
    int Slots,
    ulong TotalBytes,
    ulong MaxBytes,
    IReadOnlyList<RamType> Types,
    int ConfiguredSpeed,
    int RatedSpeed,
    ProfileEstimate Profile,
    ChannelEstimate Channels,
    bool Ecc,
    IReadOnlyList<double> Volts);

public sealed record RamInventory(RamSummary Summary, IReadOnlyList<RamModule> Modules);

public sealed record RamReport(ReadResult<RamInventory> Inventory, ReadResult<RamUsage> Usage);
