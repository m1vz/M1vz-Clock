using M1vClock.Core.Results;

namespace M1vClock.Hardware.Power;

public enum PowerPlanKind
{
    Custom,
    Balanced,
    HighPerformance,
    PowerSaver,
    Ultimate,
}

public sealed record PowerPlan(Guid Id, string Name, PowerPlanKind Kind)
{
    public bool IsKnownAndSlow => Kind is PowerPlanKind.Balanced or PowerPlanKind.PowerSaver;
}

public enum BatteryChemistry
{
    Unknown,
    LeadAcid,
    NiCd,
    NiMh,
    LithiumIon,
    ZincAir,
    LithiumPolymer,
}

public enum BatteryState
{
    Unknown,
    Discharging,
    OnAc,
    FullyCharged,
    Low,
    Critical,
    Charging,
    PartlyCharged,
}

public sealed record BatteryInfo(
    int Index,
    string? Manufacturer,
    string? Model,
    BatteryChemistry Chemistry,
    BatteryState State,
    int? ChargePercent,
    TimeSpan? EstimatedRuntime,
    double? DesignWattHours,
    double? FullWattHours,
    int? Cycles,
    double? Volts,
    double? ChargeWatts,
    double? DischargeWatts,
    bool? PowerOnline,
    bool Charging,
    bool Discharging)
{
    public double? WearPercent => DesignWattHours is > 0 && FullWattHours is > 0
        ? Math.Max(0, 100.0 * (1 - FullWattHours.Value / DesignWattHours.Value))
        : null;
}

public enum BatteryVerdict
{
    Good,
    Warning,
    Worn,
    Unknown,
}

public enum BatteryFindingKind
{
    NoCapacityData,
    WornOut,
    Moderate,
    Normal,
    Excellent,
    ManyCycles,
    LowCharge,
}

public sealed record BatteryFinding(BatteryFindingKind Kind, double Value = 0);

public sealed record BatteryAssessment(BatteryVerdict Verdict, IReadOnlyList<BatteryFinding> Findings);

public sealed record BatteryReport(BatteryInfo Battery, BatteryAssessment Assessment);

public enum PowerSource
{
    DesktopMains,
    Mains,
    Battery,
}

public sealed record BatterySet(IReadOnlyList<BatteryInfo> Batteries, bool OnMains)
{
    public PowerSource Source => Batteries.Count == 0 ? PowerSource.DesktopMains : OnMains ? PowerSource.Mains : PowerSource.Battery;
}

public sealed record PowerReport(ReadResult<IReadOnlyList<BatteryReport>> Batteries, PowerSource Source, ReadResult<PowerPlan> Plan);
