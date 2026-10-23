using M1vClock.Core.Results;

namespace M1vClock.Diagnostics.Logs;

public enum LogSeverity
{
    Verbose,
    Debug,
    Information,
    Warning,
    Error,
    Fatal,
}

public sealed record LogEntry(DateTimeOffset Time, LogSeverity Severity, string Source, string Message, string? Details)
{
    public string ShortSource => Source.Contains('.', StringComparison.Ordinal) ? Source[(Source.LastIndexOf('.') + 1)..] : Source;

    public string ToPlainText() => Details is null
        ? $"{Time:yyyy-MM-dd HH:mm:ss.fff zzz} [{Severity}] {Source}: {Message}"
        : $"{Time:yyyy-MM-dd HH:mm:ss.fff zzz} [{Severity}] {Source}: {Message}{Environment.NewLine}{Details}";
}

public sealed record LogFile(string Path, string Name, long Length, DateTimeOffset Modified);

public sealed record LogSlice(LogFile File, IReadOnlyList<LogEntry> Entries, bool StartsMidFile);

public interface ILogSource
{
    ReadResult<IReadOnlyList<LogFile>> List();

    ReadResult<LogRead> Read(LogFile file, long maximumBytes, CancellationToken cancellationToken);
}

public sealed record LogRead(IReadOnlyList<string> Lines, bool StartsMidFile);
