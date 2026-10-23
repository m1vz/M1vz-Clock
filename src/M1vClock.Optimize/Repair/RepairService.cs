using System.Globalization;
using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Repair;

public sealed class RepairService(IDismSource dism, ISfcSource sfc, TimeProvider clock, ILogger<RepairService> logger) : IRepairService
{
    public async Task<RepairResult> RunAsync(RepairCheck check, IProgress<int> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var started = clock.GetTimestamp();
        RepairResult result;
        try
        {
            result = check == RepairCheck.SystemFiles
                ? FromSfc(await sfc.VerifyAsync(progress, cancellationToken).ConfigureAwait(false), clock.GetElapsedTime(started))
                : FromDism(check, await Task.Run(() => dism.CheckHealth(check == RepairCheck.ComponentsFull, progress, cancellationToken), cancellationToken).ConfigureAwait(false), clock.GetElapsedTime(started));
        }
        catch (OperationCanceledException)
        {
            result = new RepairResult(check, RepairVerdict.Cancelled, clock.GetElapsedTime(started), null);
        }

        RepairLog.Finished(logger, check, result.Verdict);
        return result;
    }

    private static RepairResult FromDism(RepairCheck check, DismOutcome outcome, TimeSpan elapsed)
    {
        var verdict = outcome.Status switch
        {
            DismStatus.NeedsAdministrator => RepairVerdict.NeedsAdministrator,
            DismStatus.Cancelled => RepairVerdict.Cancelled,
            DismStatus.Failed => RepairVerdict.Failed,
            _ => outcome.Health switch
            {
                DismHealth.Repairable => RepairVerdict.Repairable,
                DismHealth.NotRepairable => RepairVerdict.NotRepairable,
                _ => RepairVerdict.Healthy,
            },
        };
        var detail = verdict == RepairVerdict.Failed ? outcome.ErrorCode.ToString("X8", CultureInfo.InvariantCulture) : null;
        return new RepairResult(check, verdict, elapsed, detail);
    }

    private static RepairResult FromSfc(SfcOutcome outcome, TimeSpan elapsed)
    {
        var verdict = outcome.Status switch
        {
            SfcStatus.NeedsAdministrator => RepairVerdict.NeedsAdministrator,
            SfcStatus.Cancelled => RepairVerdict.Cancelled,
            SfcStatus.Failed => RepairVerdict.Failed,
            _ => outcome.ViolationsFound ? RepairVerdict.ProblemsFound : RepairVerdict.Healthy,
        };
        return new RepairResult(RepairCheck.SystemFiles, verdict, elapsed, outcome.Detail);
    }
}
