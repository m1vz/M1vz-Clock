using System.Text.RegularExpressions;

namespace M1vClock.Hardware.Wmi;

public static partial class WmiReference
{
    public static string? DeviceId(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var match = DeviceIdPattern().Match(reference);
        return match.Success ? match.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal) : null;
    }

    [GeneratedRegex("DeviceID=\"(.*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceIdPattern();
}
