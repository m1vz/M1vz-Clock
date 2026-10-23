namespace M1vClock.Diagnostics.Reliability;

public static class ReliabilityClassifier
{
    public static ReliabilityKind Classify(string source, int id)
    {
        ArgumentNullException.ThrowIfNull(source);
        return (source.ToUpperInvariant(), id) switch
        {
            ("MSIINSTALLER", 1033 or 11707) => ReliabilityKind.SoftwareInstalled,
            ("MSIINSTALLER", 1034 or 11724) => ReliabilityKind.SoftwareRemoved,
            ("MSIINSTALLER", 1035) => ReliabilityKind.SoftwareReconfigured,
            ("MICROSOFT-WINDOWS-WINDOWSUPDATECLIENT", 19) => ReliabilityKind.UpdateInstalled,
            ("MICROSOFT-WINDOWS-WINDOWSUPDATECLIENT", 20 or 25) => ReliabilityKind.UpdateFailed,
            ("EVENTLOG", 6008) => ReliabilityKind.UnexpectedShutdown,
            ("APPLICATION ERROR", 1000) or ("APPLICATION HANG", 1002) => ReliabilityKind.AppCrash,
            ("MICROSOFT-WINDOWS-WER-SYSTEMERRORREPORTING", 1001) => ReliabilityKind.WindowsFailure,
            _ => ReliabilityKind.Other,
        };
    }
}
