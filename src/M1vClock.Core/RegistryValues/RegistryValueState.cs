using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace M1vClock.Core.RegistryValues;

public sealed record RegistryValueState(bool KeyExists, bool ValueExists, RegistryValueKind Kind, object? Data)
{
    public static RegistryValueState Absent(bool keyExists) => new(keyExists, false, RegistryValueKind.None, null);

    public static RegistryValueState DWord(int value) => new(true, true, RegistryValueKind.DWord, value);

    public static RegistryValueState QWord(long value) => new(true, true, RegistryValueKind.QWord, value);

    public static RegistryValueState Text(string value) => new(true, true, RegistryValueKind.String, value);

    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["keyExists"] = KeyExists,
            ["valueExists"] = ValueExists,
        };

        if (!ValueExists)
        {
            return json;
        }

        json["kind"] = Kind.ToString();
        json["data"] = Kind switch
        {
            RegistryValueKind.DWord => JsonValue.Create(Convert.ToInt32(Data, System.Globalization.CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => JsonValue.Create(Convert.ToInt64(Data, System.Globalization.CultureInfo.InvariantCulture)),
            RegistryValueKind.String or RegistryValueKind.ExpandString => JsonValue.Create((string?)Data ?? string.Empty),
            RegistryValueKind.MultiString => new JsonArray(((string[]?)Data ?? []).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            RegistryValueKind.Binary => JsonValue.Create(Convert.ToBase64String((byte[]?)Data ?? [])),
            _ => throw new NotSupportedException($"Tipo de valor do registro não suportado: {Kind}."),
        };
        return json;
    }

    public static RegistryValueState FromJson(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var keyExists = json["keyExists"]?.GetValue<bool>() ?? false;
        var valueExists = json["valueExists"]?.GetValue<bool>() ?? false;
        if (!valueExists)
        {
            return Absent(keyExists);
        }

        var kind = Enum.Parse<RegistryValueKind>(json["kind"]?.GetValue<string>() ?? throw new FormatException("Estado sem 'kind'."));
        var data = json["data"] ?? throw new FormatException("Estado sem 'data'.");
        object value = kind switch
        {
            RegistryValueKind.DWord => data.GetValue<int>(),
            RegistryValueKind.QWord => data.GetValue<long>(),
            RegistryValueKind.String or RegistryValueKind.ExpandString => data.GetValue<string>(),
            RegistryValueKind.MultiString => data.AsArray().Select(n => n?.GetValue<string>() ?? string.Empty).ToArray(),
            RegistryValueKind.Binary => Convert.FromBase64String(data.GetValue<string>()),
            _ => throw new NotSupportedException($"Tipo de valor do registro não suportado: {kind}."),
        };
        return new RegistryValueState(true, true, kind, value);
    }
}
