using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Diagnostics.Reliability;

public sealed class WmiReliabilitySource(ILogger<WmiReliabilitySource> logger) : IReliabilitySource
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);

    public ReadResult<IReadOnlyList<StabilityPoint>> ReadStability(TimeSpan window, CancellationToken cancellationToken)
    {
        return Query("Win32_ReliabilityStabilityMetrics", window, "stability", row =>
        {
            var at = Time(row["EndMeasurementDate"]);
            var index = row["SystemStabilityIndex"] is { } value ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : double.NaN;
            return at is { } time && double.IsFinite(index) ? new StabilityPoint(time, index) : null;
        }, cancellationToken);
    }

    public ReadResult<IReadOnlyList<ReliabilityRecord>> ReadRecords(TimeSpan window, CancellationToken cancellationToken)
    {
        return Query("Win32_ReliabilityRecords", window, "records", row =>
        {
            var at = Time(row["TimeGenerated"]);
            var source = row["SourceName"] as string;
            if (at is not { } time || string.IsNullOrWhiteSpace(source) || row["EventIdentifier"] is not { } rawId)
            {
                return null;
            }

            var id = Convert.ToInt32(rawId, CultureInfo.InvariantCulture);
            return new ReliabilityRecord(time, source, id, (row["ProductName"] as string)?.Trim() ?? string.Empty, ReliabilityClassifier.Classify(source, id));
        }, cancellationToken);
    }

    private static DateTimeOffset? Time(object? value) =>
        value is string text && text.Length > 0 ? new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(text)) : null;

    private ReadResult<IReadOnlyList<T>> Query<T>(string className, TimeSpan window, string part, Func<ManagementBaseObject, T?> map, CancellationToken cancellationToken)
        where T : class
    {
        var field = className == "Win32_ReliabilityRecords" ? "TimeGenerated" : "EndMeasurementDate";
        var since = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.Now - window);
        var query = new ObjectQuery($"SELECT * FROM {className} WHERE {field} >= '{since}'");
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\cimv2"), query, new System.Management.EnumerationOptions { Timeout = QueryTimeout, ReturnImmediately = false });
            var items = new List<T>();
            foreach (var row in searcher.Get())
            {
                using (row)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (map(row) is { } item)
                    {
                        items.Add(item);
                    }
                }
            }

            return ReadResult.Success<IReadOnlyList<T>>(items);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            DiagnosticsLog.ReliabilityFailed(logger, ex, part);
            return ReadResult.Failure<IReadOnlyList<T>>(ex.Message);
        }
    }
}
