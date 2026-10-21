using System.Text.Json.Nodes;
using M1vClock.Core.Journal;

namespace M1vClock.Core.RegistryValues;

public sealed class RegistryValueHandler(IRegistryAccess registry) : IChangeHandler
{
    public const string KindName = "registry-value";

    public string Kind => KindName;

    public JsonObject Read(JsonObject address) =>
        registry.Read(RegistryValueAddress.FromJson(address)).ToJson();

    public void Write(JsonObject address, JsonObject state) =>
        registry.Write(RegistryValueAddress.FromJson(address), RegistryValueState.FromJson(state));

    public bool StatesMatch(JsonObject actual, JsonObject expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        var actualExists = actual["valueExists"]?.GetValue<bool>() ?? false;
        var expectedExists = expected["valueExists"]?.GetValue<bool>() ?? false;
        if (!actualExists && !expectedExists)
        {
            return true;
        }

        return actualExists == expectedExists
            && JsonNode.DeepEquals(actual["kind"], expected["kind"])
            && JsonNode.DeepEquals(actual["data"], expected["data"]);
    }
}

public static class RegistryValueChanges
{
    public static SystemChange Set(string module, string title, RegistryValueAddress address, RegistryValueState desired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(desired);
        return new SystemChange(
            RegistryValueHandler.KindName,
            module,
            title,
            address.Target,
            address.Display,
            address.ToJson(),
            desired.ToJson());
    }
}
