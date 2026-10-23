using System.Management;
using System.Runtime.InteropServices;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Services;

public sealed class WmiServiceSource(ILogger<WmiServiceSource> logger) : IServiceSource
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);

    public ReadResult<IReadOnlyList<ServiceRaw>> Read(CancellationToken cancellationToken)
    {
        var query = new ObjectQuery("SELECT Name, DisplayName, Description, State, StartMode, DelayedAutoStart, StartName, PathName FROM Win32_Service");
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"), query, new System.Management.EnumerationOptions { Timeout = QueryTimeout, ReturnImmediately = false });
            var list = new List<ServiceRaw>();
            foreach (var row in searcher.Get())
            {
                using (row)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row["Name"] is not string name || name.Length == 0)
                    {
                        continue;
                    }

                    list.Add(new ServiceRaw(
                        name,
                        row["DisplayName"] as string ?? name,
                        row["Description"] as string,
                        row["State"] as string ?? string.Empty,
                        row["StartMode"] as string ?? string.Empty,
                        row["DelayedAutoStart"] is true,
                        row["StartName"] as string,
                        row["PathName"] as string));
                }
            }

            return ReadResult.Success<IReadOnlyList<ServiceRaw>>(list);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            OptimizeLog.ServicesFailed(logger, ex);
            return ReadResult.Failure<IReadOnlyList<ServiceRaw>>(ex.Message);
        }
    }
}
