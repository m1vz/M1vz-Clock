using System.Globalization;
using System.Text.RegularExpressions;

namespace M1vClock.Diagnostics.Logs;

public static partial class LogParser
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<level>[A-Z]{3})\] (?<source>\S+): (?<message>.*)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 250)]
    private static partial Regex Header();

    public static IReadOnlyList<LogEntry> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var entries = new List<LogEntry>();
        LogEntry? current = null;
        List<string>? details = null;

        void Close()
        {
            if (current is null)
            {
                return;
            }

            entries.Add(details is { Count: > 0 } ? current with { Details = string.Join('\n', details) } : current);
            current = null;
            details = null;
        }

        foreach (var line in lines)
        {
            if (TryHeader(line) is { } entry)
            {
                Close();
                current = entry;
                continue;
            }

            if (current is not null && line.Length > 0)
            {
                (details ??= []).Add(line);
            }
        }

        Close();
        return entries;
    }

    public static LogSeverity? SeverityOf(string code) => code switch
    {
        "VRB" => LogSeverity.Verbose,
        "DBG" => LogSeverity.Debug,
        "INF" => LogSeverity.Information,
        "WRN" => LogSeverity.Warning,
        "ERR" => LogSeverity.Error,
        "FTL" => LogSeverity.Fatal,
        _ => null,
    };

    private static LogEntry? TryHeader(string line)
    {
        if (line.Length < 30 || !char.IsAsciiDigit(line[0]))
        {
            return null;
        }

        var match = Header().Match(line);
        if (!match.Success
            || SeverityOf(match.Groups["level"].Value) is not { } severity
            || !DateTimeOffset.TryParseExact(match.Groups["time"].Value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return null;
        }

        return new LogEntry(time, severity, match.Groups["source"].Value, match.Groups["message"].Value, null);
    }
}
