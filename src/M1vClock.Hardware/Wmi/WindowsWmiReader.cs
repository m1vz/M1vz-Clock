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

    public IReadOnlyList<WmiRelation> QueryRelated(WmiQuery source, string relatedClass)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!WmiQuery.IsIdentifier(relatedClass))
        {
            throw new ArgumentException("A consulta WMI só aceita nomes de classe fixos, sem texto livre.", nameof(relatedClass));
        }

        try
        {
            return RunRelated(source, relatedClass);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            throw new UnauthorizedAccessException($"Access denied to {relatedClass}.", ex);
        }
    }

    private static List<WmiRelation> RunRelated(WmiQuery source, string relatedClass)
    {
        var scope = new ManagementScope(source.Scope, new ConnectionOptions { Timeout = ConnectTimeout });
        var options = new WmiEnumerationOptions { Timeout = QueryTimeout, ReturnImmediately = false };
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(source.ToWql()), options);
        using var results = searcher.Get();
        var relations = new List<WmiRelation>();
        foreach (var item in results)
        {
            using (item)
            {
                var related = new List<WmiRow>();
                if (item is ManagementObject managed)
                {
                    using var relatedResults = managed.GetRelated(relatedClass);
                    foreach (var other in relatedResults)
                    {
                        using (other)
                        {
                            related.Add(ToRow(other));
                        }
                    }
                }

                relations.Add(new WmiRelation(ToRow(item), related));
            }
        }

        return relations;
    }

    private static WmiRow ToRow(ManagementBaseObject item) =>
        new(item.Properties.Cast<PropertyData>().Select(p => KeyValuePair.Create<string, object?>(p.Name, p.Value)));

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
                rows.Add(ToRow(item));
            }
        }

        return rows;
    }
}
