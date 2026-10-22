using System.Management;
using System.Runtime.InteropServices;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware;

internal static class SectionReader
{
    public static ReadResult<T> Read<T>(ILogger logger, string section, Func<T> read)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            return ReadResult.Success(read());
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidOperationException or HardwareReadException or TimeoutException or IOException)
        {
            HardwareLog.SectionFailed(logger, ex, section);
            return ReadResult.Failure<T>(ex.Message);
        }
    }

    public static Task<ReadResult<T>> ReadAsync<T>(ILogger logger, string section, Func<T> read, CancellationToken cancellationToken) =>
        Task.Run(() => Read(logger, section, read), cancellationToken);
}
