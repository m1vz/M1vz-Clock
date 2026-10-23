using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using M1vClock.Core.Journal;

namespace M1vClock.Optimize.Tweaks;

public interface IPowerSchemeAccess
{
    Guid? Active();

    bool Exists(Guid scheme);

    string? NameOf(Guid scheme);

    void SetActive(Guid scheme);
}

public sealed class PowerSchemeHandler(IPowerSchemeAccess power) : IChangeHandler
{
    public const string KindName = "power-scheme";

    public const string Target = "power-scheme:active";

    public string Kind => KindName;

    public static JsonObject AddressOfActive() => new() { ["scope"] = "active" };

    public static JsonObject StateOf(Guid? scheme) => new() { ["scheme"] = scheme?.ToString("D", CultureInfo.InvariantCulture) };

    public JsonObject Read(JsonObject address) => StateOf(power.Active());

    public void Write(JsonObject address, JsonObject state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var text = state["scheme"]?.GetValue<string>();
        if (!Guid.TryParse(text, out var scheme))
        {
            throw new FormatException("Estado do plano de energia sem um identificador válido.");
        }

        power.SetActive(scheme);
    }

    public bool StatesMatch(JsonObject actual, JsonObject expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        return string.Equals(actual["scheme"]?.GetValue<string>(), expected["scheme"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed partial class WindowsPowerSchemeAccess : IPowerSchemeAccess
{
    private const uint AccessScheme = 16;
    private const uint Success = 0;
    private const uint NoMoreItems = 259;

    public Guid? Active()
    {
        if (PowerGetActiveScheme(0, out var pointer) != Success || pointer == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    public bool Exists(Guid scheme)
    {
        var buffer = new byte[16];
        for (uint index = 0; index < 256; index++)
        {
            var size = (uint)buffer.Length;
            var status = PowerEnumerate(0, 0, 0, AccessScheme, index, buffer, ref size);
            if (status == NoMoreItems || status != Success)
            {
                return false;
            }

            if (new Guid(buffer) == scheme)
            {
                return true;
            }
        }

        return false;
    }

    public string? NameOf(Guid scheme)
    {
        uint size = 0;
        if (PowerReadFriendlyName(0, in scheme, 0, 0, null, ref size) != Success || size < 2)
        {
            return null;
        }

        var buffer = new byte[size];
        if (PowerReadFriendlyName(0, in scheme, 0, 0, buffer, ref size) != Success)
        {
            return null;
        }

        var name = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return name.Length == 0 ? null : name;
    }

    public void SetActive(Guid scheme)
    {
        var status = PowerSetActiveScheme(0, in scheme);
        if (status != Success)
        {
            throw new Win32Exception(unchecked((int)status));
        }
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(nint userRootPowerKey, out nint activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveScheme(nint userRootPowerKey, in Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadFriendlyName(nint rootPowerKey, in Guid schemeGuid, nint subGroupOfPowerSettingsGuid, nint powerSettingGuid, byte[]? buffer, ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerEnumerate(nint rootPowerKey, nint schemeGuid, nint subGroupOfPowerSettingsGuid, uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
