using M1vClock.App.Hosting;
using M1vClock.Benchmarks.Results;
using M1vClock.Diagnostics.Crashes;
using M1vClock.Diagnostics.Events;
using M1vClock.Diagnostics.Reliability;
using M1vClock.Hardware.Gpu;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Power;
using M1vClock.Hardware.Ram;
using M1vClock.Hardware.Storage;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Services.Reports;

public interface IReportCollector
{
    Task<ReportDocument> CollectAsync(CancellationToken cancellationToken);
}

public sealed class ReportCollector(
    IPlatformService platform,
    IGpuService gpu,
    IRamService ram,
    IStorageService storage,
    IPowerService power,
    IEventService events,
    ICrashService crashes,
    IReliabilityService reliability,
    BenchmarkHistory history,
    GpuSpecDatabase database,
    AppInfo info,
    TimeProvider clock,
    ILogger<ReportCollector> logger) : IReportCollector
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    public async Task<ReportDocument> CollectAsync(CancellationToken cancellationToken)
    {
        var sections = await Task.WhenAll(
            Guard(ReportSectionIds.System, async () => ReportSections.System(await platform.ReadAsync(cancellationToken).ConfigureAwait(false))),
            Guard(ReportSectionIds.Gpu, async () => ReportSections.Gpu(await gpu.ReadAsync(cancellationToken).ConfigureAwait(false), database)),
            Guard(ReportSectionIds.Memory, async () => ReportSections.Memory(await ram.ReadAsync(cancellationToken).ConfigureAwait(false))),
            Guard(ReportSectionIds.Storage, async () => ReportSections.Storage(await storage.ReadAsync(cancellationToken).ConfigureAwait(false))),
            Guard(ReportSectionIds.Power, async () => ReportSections.Power(await power.ReadAsync(cancellationToken).ConfigureAwait(false))),
            Guard(ReportSectionIds.Events, async () => ReportSections.Events(await events.ReadAsync(Window, cancellationToken).ConfigureAwait(false))),
            Guard(ReportSectionIds.Crashes, async () => ReportSections.Crashes(await crashes.ReadAsync(Window, cancellationToken).ConfigureAwait(false))),
            Guard(ReportSectionIds.Reliability, async () => ReportSections.Reliability(await reliability.ReadAsync(Window, cancellationToken).ConfigureAwait(false))),
            Guard(ReportSectionIds.Benchmarks, () => Task.FromResult(ReportSections.Benchmarks(history.Records)))).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new ReportDocument(clock.GetUtcNow(), info.Version, null, sections);
    }

    private async Task<ReportSection> Guard(string id, Func<Task<ReportSection>> build)
    {
        try
        {
            return await build().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            AppLog.ReportSectionFailed(logger, ex, id);
            return new ReportSection(id, Localization.Loc.Get($"Diag.Report.Section.{id}"), [], [ReportSections.NotRead(ex.Message)]);
        }
    }
}
