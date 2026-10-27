using System.Text.Json;
using M1vClock.Core.IO;
using M1vClock.Tuning.Gpu;
using M1vClock.Tuning.Stability;
using Microsoft.Extensions.Logging;

namespace M1vClock.Tuning;

public sealed record TuningMarker(GpuVendor Vendor, int Index, string Device, DateTimeOffset AppliedAt, int? PowerWatts, int? ClockCapMegahertz);

public enum GuardDecision
{
    Nothing,
    KeepApplied,
    RestoreDefaults,
}

public enum GuardReason
{
    None,
    PreviousSessionCrashed,
    DriverReset,
    UnexpectedShutdown,
}

public sealed record GuardCheck(GuardDecision Decision, GuardReason Reason, TuningMarker? Marker);

public static class GuardRules
{
    public static GuardCheck Decide(TuningMarker? marker, bool previousSessionCrashed, HardwareErrorReport errorsSinceApplied)
    {
        ArgumentNullException.ThrowIfNull(errorsSinceApplied);
        if (marker is null)
        {
            return new GuardCheck(GuardDecision.Nothing, GuardReason.None, null);
        }

        var reason = errorsSinceApplied.UnexpectedShutdowns > 0 ? GuardReason.UnexpectedShutdown
            : errorsSinceApplied.DriverResets > 0 ? GuardReason.DriverReset
            : previousSessionCrashed ? GuardReason.PreviousSessionCrashed
            : GuardReason.None;
        return new GuardCheck(reason == GuardReason.None ? GuardDecision.KeepApplied : GuardDecision.RestoreDefaults, reason, marker);
    }
}

public sealed class SessionGuard(string markerFile, IHardwareErrorSource errors, IGpuTuner tuner, ILogger<SessionGuard> logger)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Record(TuningMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        AtomicFile.WriteAllText(markerFile, JsonSerializer.Serialize(marker, Options));
    }

    public void Clear()
    {
        if (File.Exists(markerFile))
        {
            File.Delete(markerFile);
        }
    }

    public TuningMarker? Load()
    {
        if (!File.Exists(markerFile))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TuningMarker>(File.ReadAllText(markerFile), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            TuningLog.MarkerUnreadable(logger, ex, markerFile);
            return null;
        }
    }

    public GuardCheck CheckAtStartup(bool previousSessionCrashed)
    {
        var marker = Load();
        var check = GuardRules.Decide(marker, previousSessionCrashed, marker is null ? HardwareErrorReport.Empty : errors.ErrorsSince(marker.AppliedAt));
        if (check.Decision != GuardDecision.RestoreDefaults || marker is null)
        {
            return check;
        }

        var result = tuner.RestoreDefaults(marker.Index);
        TuningLog.GuardRestored(logger, check.Reason, result.Status);
        if (result.Status is NvidiaApplyStatus.Applied or NvidiaApplyStatus.NoDevice or NvidiaApplyStatus.LibraryMissing)
        {
            Clear();
        }

        return check;
    }
}
