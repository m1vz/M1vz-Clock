using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Repair;

public sealed partial class WindowsDismSource(ILogger<WindowsDismSource> logger) : IDismSource
{
    private const string OnlineImage = "DISM_{53BFAE52-B167-4E2F-A258-0A37B57FF845}";
    private const int LogErrorsOnly = 0;
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int ElevationRequired = unchecked((int)0x800702E4);
    private const int Cancelled = unchecked((int)0x800704C7);
    private const int OperationAborted = unchecked((int)0x800703E3);

    private static readonly Lock Gate = new();

    public DismOutcome CheckHealth(bool scan, IProgress<int> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        lock (Gate)
        {
            var started = DismInitialize(LogErrorsOnly, null, null);
            if (started != 0)
            {
                return Describe(started, DismHealth.Healthy);
            }

            try
            {
                return Check(scan, progress, cancellationToken);
            }
            finally
            {
                _ = DismShutdown();
            }
        }
    }

    private static DismOutcome Describe(int code, DismHealth health)
    {
        if (code == 0)
        {
            return new DismOutcome(DismStatus.Done, health, 0);
        }

        if (code is AccessDenied or ElevationRequired)
        {
            return new DismOutcome(DismStatus.NeedsAdministrator, DismHealth.Healthy, code);
        }

        return code is Cancelled or OperationAborted
            ? new DismOutcome(DismStatus.Cancelled, DismHealth.Healthy, code)
            : new DismOutcome(DismStatus.Failed, DismHealth.Healthy, code);
    }

    [UnmanagedCallersOnly]
    private static void OnProgress(uint current, uint total, nint userData)
    {
        if (total > 0 && GCHandle.FromIntPtr(userData).Target is IProgress<int> sink)
        {
            sink.Report((int)Math.Min(100, current * 100UL / total));
        }
    }

    private unsafe DismOutcome Check(bool scan, IProgress<int> progress, CancellationToken cancellationToken)
    {
        var opened = DismOpenSession(OnlineImage, null, null, out var session);
        if (opened != 0)
        {
            return Describe(opened, DismHealth.Healthy);
        }

        using var cancel = new ManualResetEvent(false);
        using var registration = cancellationToken.Register(() => cancel.Set());
        var handle = GCHandle.Alloc(progress);
        try
        {
            var code = DismCheckImageHealth(session, scan, cancel.SafeWaitHandle.DangerousGetHandle(), &OnProgress, GCHandle.ToIntPtr(handle), out var health);
            var outcome = Describe(code, health switch
            {
                1 => DismHealth.Repairable,
                2 => DismHealth.NotRepairable,
                _ => DismHealth.Healthy,
            });
            if (outcome.Status == DismStatus.Failed)
            {
                RepairLog.DismFailed(logger, code);
            }

            return outcome;
        }
        finally
        {
            handle.Free();
            _ = DismCloseSession(session);
        }
    }

    [LibraryImport("DismApi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DismInitialize(int logLevel, string? logFilePath, string? scratchDirectory);

    [LibraryImport("DismApi.dll")]
    private static partial int DismShutdown();

    [LibraryImport("DismApi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DismOpenSession(string imagePath, string? windowsDirectory, string? systemDrive, out uint session);

    [LibraryImport("DismApi.dll")]
    private static partial int DismCloseSession(uint session);

    [LibraryImport("DismApi.dll")]
    private static unsafe partial int DismCheckImageHealth(
        uint session,
        [MarshalAs(UnmanagedType.Bool)] bool scanImage,
        nint cancelEvent,
        delegate* unmanaged<uint, uint, nint, void> progress,
        nint userData,
        out int imageHealth);
}
