using System.Management;
using WmiEnumerationOptions = System.Management.EnumerationOptions;

namespace M1vClock.Hardware.Wmi;

public sealed class WindowsWmiReader : IWmiReader
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(20);

    public IReadOnlyList<WmiRow> Query(WmiQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            return Run(query);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            throw new UnauthorizedAccessException($"Access denied to {query.ClassName}.", ex);
        }
    }

    private static List<WmiRow> Run(WmiQuery query)
    {
        var scope = new ManagementScope(query.Scope, new ConnectionOptions { Timeout = ConnectTimeout });
        var options = new WmiEnumerationOptions { Timeout = QueryTimeout, ReturnImmediately = false };
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query.ToWql()), options);
        using var results = searcher.Get();
        var rows = new List<WmiRow>();
        foreach (var item in results)
        {
            using (item)
            {
                rows.Add(new WmiRow(item.Properties.Cast<PropertyData>().Select(p => KeyValuePair.Create<string, object?>(p.Name, p.Value))));
            }
        }

        return rows;
    }
}
