using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace M1vClock.Core.RegistryValues;

public sealed record RegistryValueAddress(RegistryHive Hive, RegistryView View, string KeyPath, string ValueName)
{
    public string Target =>
        string.Create(CultureInfo.InvariantCulture, $"registry:{HiveName}:{View}:{KeyPath.ToUpperInvariant()}:{ValueName.ToUpperInvariant()}");

    public string Display => string.IsNullOrEmpty(ValueName) ? $@"{HiveName}\{KeyPath}" : $@"{HiveName}\{KeyPath}\{ValueName}";

    private string HiveName => Hive switch
    {
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        RegistryHive.ClassesRoot => "HKCR",
        RegistryHive.Users => "HKU",
        RegistryHive.CurrentConfig => "HKCC",
        _ => Hive.ToString(),
    };

    public JsonObject ToJson() => new()
    {
        ["hive"] = Hive.ToString(),
        ["view"] = View.ToString(),
        ["key"] = KeyPath,
        ["name"] = ValueName,
    };

    public static RegistryValueAddress FromJson(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return new RegistryValueAddress(
            Enum.Parse<RegistryHive>(Required(json, "hive")),
            Enum.Parse<RegistryView>(Required(json, "view")),
            Required(json, "key"),
            json["name"]?.GetValue<string>() ?? string.Empty);
    }

    private static string Required(JsonObject json, string property) =>
        json[property]?.GetValue<string>() ?? throw new FormatException($"Endereço de registro sem '{property}'.");
}
