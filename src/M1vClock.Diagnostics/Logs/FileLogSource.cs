using System.Text;
using M1vClock.Core.Paths;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Diagnostics.Logs;

public sealed class FileLogSource(AppPaths paths, ILogger<FileLogSource> logger) : ILogSource
{
    public ReadResult<IReadOnlyList<LogFile>> List()
    {
        try
        {
            var folder = new DirectoryInfo(paths.Logs);
            if (!folder.Exists || folder.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return ReadResult.Success<IReadOnlyList<LogFile>>([]);
            }

            var files = folder.EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
                .Where(f => !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
                .Select(f => new LogFile(f.FullName, f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
                .ToList();
            return ReadResult.Success<IReadOnlyList<LogFile>>(files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticsLog.LogFolderFailed(logger, ex);
            return ReadResult.Failure<IReadOnlyList<LogFile>>(ex.Message);
        }
    }

    public ReadResult<LogRead> Read(LogFile file, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        if (!IsInsideLogs(file.Path))
        {
            return ReadResult.Failure<LogRead>("O arquivo não está na pasta de logs do m1vClock.");
        }

        try
        {
            using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var midFile = stream.Length > maximumBytes;
            if (midFile)
            {
                stream.Seek(-maximumBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            var lines = new List<string>();
            if (midFile)
            {
                reader.ReadLine();
            }

            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lines.Add(line);
            }

            return ReadResult.Success(new LogRead(lines, midFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticsLog.LogFileFailed(logger, ex, file.Name);
            return ReadResult.Failure<LogRead>(ex.Message);
        }
    }

    private bool IsInsideLogs(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(paths.Logs) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetDirectoryName(full) + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase);
    }
}
