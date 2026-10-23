using System.Globalization;
using System.Text.RegularExpressions;

namespace M1vClock.Optimize.Repair;

public static partial class SfcOutput
{
    private const int ShownFiles = 5;
    private const string VerifyComplete = "Verify complete";

    private static readonly string[] Routine =
    [
        "Verifying N components",
        "Beginning Verify and Repair transaction",
        VerifyComplete,
    ];

    [GeneratedRegex(@"(\d{1,3})\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"\bCorrupt file:\s*(?<path>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CorruptFilePattern();

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    public static int? LastPercent(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var matches = PercentPattern().Matches(text);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            if (int.TryParse(matches[i].Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= 100)
            {
                return value;
            }
        }

        return null;
    }

    public static bool IsRelevant(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Contains("[SR]", StringComparison.Ordinal) || line.Contains("Corrupt file:", StringComparison.Ordinal);
    }

    public static SfcOutcome Analyze(int exitCode, IReadOnlyList<string> logLines)
    {
        ArgumentNullException.ThrowIfNull(logLines);
        var completed = 0;
        var unusual = 0;
        var cannotRepair = false;
        var files = new List<string>();
        foreach (var line in logLines)
        {
            var corrupt = CorruptFilePattern().Match(line);
            if (corrupt.Success)
            {
                var name = Path.GetFileName(corrupt.Groups["path"].Value.TrimEnd('\\', '/'));
                if (name.Length > 0 && !files.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    files.Add(name);
                }

                continue;
            }

            var marker = line.IndexOf("[SR]", StringComparison.Ordinal);
            if (marker < 0)
            {
                continue;
            }

            var payload = line[(marker + 4)..].Trim();
            var shape = Digits().Replace(payload, "N");
            if (Array.Exists(Routine, r => string.Equals(r, shape, StringComparison.Ordinal)))
            {
                if (string.Equals(shape, VerifyComplete, StringComparison.Ordinal))
                {
                    completed++;
                }

                continue;
            }

            unusual++;
            cannotRepair |= payload.Contains("Cannot repair", StringComparison.OrdinalIgnoreCase);
        }

        if (files.Count > 0 || unusual > 0)
        {
            var shown = string.Join(", ", files.Take(ShownFiles)) + (files.Count > ShownFiles ? ", …" : string.Empty);
            return new SfcOutcome(SfcStatus.Done, exitCode, true, cannotRepair, shown.Length == 0 ? null : shown);
        }

        if (completed == 0 || exitCode != 0)
        {
            return new SfcOutcome(SfcStatus.Failed, exitCode, false, false, exitCode.ToString(CultureInfo.InvariantCulture));
        }

        return new SfcOutcome(SfcStatus.Done, exitCode, false, false, null);
    }
}
