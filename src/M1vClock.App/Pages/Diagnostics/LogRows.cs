using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Diagnostics.Logs;

namespace M1vClock.App.Pages.Diagnostics;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        TimeText = entry.Time.ToLocalTime().ToString("T", Loc.Culture);
        SeverityText = Loc.Get($"Diag.Logs.Severity.{entry.Severity}");
        Tone = entry.Severity switch
        {
            LogSeverity.Fatal or LogSeverity.Error => MetricState.Critical,
            LogSeverity.Warning => MetricState.Warning,
            _ => MetricState.Neutral,
        };
    }

    public LogEntry Entry { get; }

    public string TimeText { get; }

    public string SeverityText { get; }

    public MetricState Tone { get; }

    public string Source => Entry.ShortSource;

    public string Message => Entry.Message;

    public string Details => Entry.Details ?? string.Empty;

    public bool HasDetails => Entry.Details is not null;

    public bool IsProblem => Entry.Severity >= LogSeverity.Warning;

    public string AccessibleName => $"{SeverityText}, {TimeText}, {Source}: {Message}";

    public override string ToString() => AccessibleName;
}

public static class LogRows
{
    public const int MaximumShown = 300;

    public static string FileLabel(LogFile file, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(file);
        var day = file.Modified.ToLocalTime().Date;
        var today = now.ToLocalTime().Date;
        var name = day == today
            ? Loc.Get("Diag.Logs.File.Today")
            : day == today.AddDays(-1)
                ? Loc.Get("Diag.Logs.File.Yesterday")
                : day.ToString("dd/MM", Loc.Culture);
        return Loc.Format("Diag.Logs.File.Label", name, Units.Bytes(file.Length, Loc.Culture));
    }

    public static IReadOnlyList<LogEntryViewModel> Filter(IReadOnlyList<LogEntry> entries, int levelIndex, string search, out int matching)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var minimum = levelIndex switch
        {
            1 => LogSeverity.Warning,
            2 => LogSeverity.Error,
            _ => LogSeverity.Verbose,
        };
        var text = (search ?? string.Empty).Trim();
        var found = entries
            .Where(e => e.Severity >= minimum)
            .Where(e => text.Length == 0
                || e.Message.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || e.Source.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || (e.Details?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .Reverse()
            .ToList();
        matching = found.Count;
        return found.Take(MaximumShown).Select(e => new LogEntryViewModel(e)).ToList();
    }

    public static string Summary(IReadOnlyList<LogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return Loc.Format(
            "Diag.Logs.Summary",
            entries.Count.ToString("N0", Loc.Culture),
            entries.Count(e => e.Severity == LogSeverity.Warning).ToString("N0", Loc.Culture),
            entries.Count(e => e.Severity >= LogSeverity.Error).ToString("N0", Loc.Culture));
    }

    public static string PlainText(IEnumerable<LogEntryViewModel> shown)
    {
        ArgumentNullException.ThrowIfNull(shown);
        return string.Join(Environment.NewLine, shown.Select(e => e.Entry.ToPlainText()));
    }
}
