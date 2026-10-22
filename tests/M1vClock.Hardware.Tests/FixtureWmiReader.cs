using System.Text.Json;
using M1vClock.Hardware.Wmi;

namespace M1vClock.Hardware.Tests;

internal sealed class FixtureWmiReader : IWmiReader
{
    private readonly Dictionary<string, List<WmiRow>> _classes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<WmiRelation>> _related = new(StringComparer.OrdinalIgnoreCase);

    private FixtureWmiReader()
    {
    }

    public static FixtureWmiReader Load(string name)
    {
        var resource = $"Fixtures.{name}.json";
        using var stream = typeof(FixtureWmiReader).Assembly.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"Arquivo de exemplo não encontrado: {resource}");
        using var document = JsonDocument.Parse(stream);
        var reader = new FixtureWmiReader();
        foreach (var entry in document.RootElement.GetProperty("classes").EnumerateArray())
        {
            reader._classes[Key(entry.GetProperty("scope").GetString(), entry.GetProperty("class").GetString())] =
                [.. entry.GetProperty("rows").EnumerateArray().Select(ToRow)];
        }

        foreach (var entry in document.RootElement.GetProperty("related").EnumerateArray())
        {
            reader._related[Key(entry.GetProperty("scope").GetString(), entry.GetProperty("source").GetString(), entry.GetProperty("relatedClass").GetString())] =
                [.. entry.GetProperty("items").EnumerateArray().Select(item => new WmiRelation(
                    ToRow(item.GetProperty("source")),
                    [.. item.GetProperty("related").EnumerateArray().Select(ToRow)]))];
        }

        return reader;
    }

    public static JsonDocument LoadRaw(string name)
    {
        using var stream = typeof(FixtureWmiReader).Assembly.GetManifestResourceStream($"Fixtures.{name}.json")
            ?? throw new FileNotFoundException($"Arquivo de exemplo não encontrado: Fixtures.{name}.json");
        return JsonDocument.Parse(stream);
    }

    public IReadOnlyList<WmiRow> Query(WmiQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _classes.TryGetValue(Key(query.Scope, query.ClassName), out var rows) ? rows : [];
    }

    public IReadOnlyList<WmiRelation> QueryRelated(WmiQuery source, string relatedClass)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _related.TryGetValue(Key(source.Scope, source.ClassName, relatedClass), out var relations) ? relations : [];
    }

    private static string Key(string? scope, string? className, string? relatedClass = null) =>
        string.Join('|', scope, className, relatedClass);

    private static WmiRow ToRow(JsonElement element) =>
        new(element.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, Convert(p.Value))));

    private static object? Convert(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var whole) => whole,
        JsonValueKind.Number when value.TryGetUInt64(out var big) => big,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(Convert).ToArray(),
        _ => null,
    };
}
