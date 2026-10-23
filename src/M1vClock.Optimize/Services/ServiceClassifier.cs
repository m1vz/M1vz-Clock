using System.Text.RegularExpressions;

namespace M1vClock.Optimize.Services;

public static class ServiceClassifier
{
    private static readonly string[] AntiCheat =
    [
        "EasyAntiCheat", "BattlEye", "BEService", "Vanguard", "FACEIT", "ESEA", "PunkBuster", "Ricochet",
    ];

    private static readonly string[] AntiCheatExactNames = ["vgc", "vgk"];

    private static readonly string[] Security =
    [
        "Kaspersky", "AVP", "Avast", "AVG", "Norton", "McAfee", "Bitdefender", "ESET", "Malwarebytes", "Sophos",
        "Trend Micro", "F-Secure", "Windows Defender", "Microsoft Defender", "Windows Security", "WinDefend",
    ];

    private static readonly Regex AntiCheatPattern = Whole(AntiCheat);

    private static readonly Regex SecurityPattern = Whole(Security);

    public static ServiceStartMode ModeOf(string startMode, bool delayedAutoStart) => startMode?.Trim().ToUpperInvariant() switch
    {
        "BOOT" => ServiceStartMode.Boot,
        "SYSTEM" => ServiceStartMode.System,
        "AUTO" => delayedAutoStart ? ServiceStartMode.AutomaticDelayed : ServiceStartMode.Automatic,
        "MANUAL" => ServiceStartMode.Manual,
        "DISABLED" => ServiceStartMode.Disabled,
        _ => ServiceStartMode.Unknown,
    };

    public static ServiceRunState StateOf(string state) => state?.Trim().ToUpperInvariant() switch
    {
        "RUNNING" => ServiceRunState.Running,
        "STOPPED" => ServiceRunState.Stopped,
        _ => ServiceRunState.Other,
    };

    public static ServiceOrigin OriginOf(string? imagePath, string? company, string windowsDirectory)
    {
        ArgumentNullException.ThrowIfNull(windowsDirectory);
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return ServiceOrigin.Microsoft;
        }

        if (windowsDirectory.Length > 0 && imagePath.StartsWith(windowsDirectory + @"\", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceOrigin.Microsoft;
        }

        return company is not null && company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ? ServiceOrigin.Microsoft : ServiceOrigin.ThirdParty;
    }

    public static ProtectionReason ProtectionOf(string name, string displayName, string? company, string? imagePath, ServiceOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(displayName);
        var haystack = string.Join('\n', name, displayName, company ?? string.Empty, imagePath ?? string.Empty);
        if (AntiCheatPattern.IsMatch(haystack) || AntiCheatExactNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return ProtectionReason.AntiCheat;
        }

        if (SecurityPattern.IsMatch(haystack))
        {
            return ProtectionReason.SecuritySoftware;
        }

        return origin == ServiceOrigin.Microsoft ? ProtectionReason.Windows : ProtectionReason.None;
    }

    private static Regex Whole(string[] words) =>
        new("(?<![A-Za-z0-9])(" + string.Join("|", words.Select(Regex.Escape)) + ")(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
}
