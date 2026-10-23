namespace M1vClock.Optimize.Repair;

public enum RepairCheck
{
    ComponentsQuick,
    ComponentsFull,
    SystemFiles,
}

public enum RepairVerdict
{
    Healthy,
    Repairable,
    NotRepairable,
    ProblemsFound,
    NeedsAdministrator,
    Cancelled,
    Failed,
}

public sealed record RepairResult(RepairCheck Check, RepairVerdict Verdict, TimeSpan Elapsed, string? Detail)
{
    public bool IsClean => Verdict == RepairVerdict.Healthy;

    public bool NeedsAction => Verdict is RepairVerdict.Repairable or RepairVerdict.NotRepairable or RepairVerdict.ProblemsFound;
}

public enum DismHealth
{
    Healthy,
    Repairable,
    NotRepairable,
}

public enum DismStatus
{
    Done,
    NeedsAdministrator,
    Cancelled,
    Failed,
}

public sealed record DismOutcome(DismStatus Status, DismHealth Health, int ErrorCode);

public interface IDismSource
{
    DismOutcome CheckHealth(bool scan, IProgress<int> progress, CancellationToken cancellationToken);
}

public enum SfcStatus
{
    Done,
    NeedsAdministrator,
    Cancelled,
    Failed,
}

public sealed record SfcOutcome(SfcStatus Status, int ExitCode, bool ViolationsFound, bool CouldNotRepair, string? Detail);

public interface ISfcSource
{
    Task<SfcOutcome> VerifyAsync(IProgress<int> progress, CancellationToken cancellationToken);
}

public interface IRepairService
{
    Task<RepairResult> RunAsync(RepairCheck check, IProgress<int> progress, CancellationToken cancellationToken);
}
