using M1vClock.Core.Results;

namespace M1vClock.Diagnostics.Logs;

public interface ILogService
{
    Task<ReadResult<IReadOnlyList<LogFile>>> ListAsync(CancellationToken cancellationToken);

    Task<ReadResult<LogSlice>> ReadAsync(LogFile file, CancellationToken cancellationToken);
}

public sealed class LogService(ILogSource source) : ILogService
{
    public const long MaximumBytes = 4 * 1024 * 1024;

    public Task<ReadResult<IReadOnlyList<LogFile>>> ListAsync(CancellationToken cancellationToken) =>
        Task.Run(() => source.List().Map<IReadOnlyList<LogFile>>(files => files.OrderByDescending(f => f.Modified).ThenByDescending(f => f.Name, StringComparer.Ordinal).ToList()), cancellationToken);

    public Task<ReadResult<LogSlice>> ReadAsync(LogFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Task.Run(() => source.Read(file, MaximumBytes, cancellationToken).Map(read => new LogSlice(file, LogParser.Parse(read.Lines), read.StartsMidFile)), cancellationToken);
    }
}
