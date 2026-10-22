using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace M1vClock.Diagnostics.Crashes;

public static class CrashParsers
{
    private static readonly XNamespace EventNamespace = "http://schemas.microsoft.com/win/2004/08/events/event";

    public static UnexpectedShutdown? ParseKernelPower(string xml)
    {
        if (!TryLoad(xml, out var root) || TimeOf(root) is not { } time)
        {
            return null;
        }

        var data = DataOf(root);
        var code = ParseNumber(data.GetValueOrDefault("BugcheckCode")) is { } c and <= uint.MaxValue ? (uint)c : 0u;
        var buttonPressed = ParseNumber(data.GetValueOrDefault("PowerButtonTimestamp")) is > 0;
        if (code != 0)
        {
            var parameters = Enumerable.Range(1, 4).Select(i => ParseNumber(data.GetValueOrDefault($"BugcheckParameter{i}")) ?? 0).ToList();
            return new UnexpectedShutdown(time, ShutdownCause.BlueScreen, new BugCheckInfo(code, parameters));
        }

        return new UnexpectedShutdown(time, buttonPressed ? ShutdownCause.PowerButton : ShutdownCause.Unknown, null);
    }

    public static AppCrash? ParseAppCrash(string xml)
    {
        if (!TryLoad(xml, out var root) || TimeOf(root) is not { } time)
        {
            return null;
        }

        var data = DataOf(root);
        var app = data.GetValueOrDefault("AppName");
        if (string.IsNullOrWhiteSpace(app))
        {
            return null;
        }

        var exception = ParseHex(data.GetValueOrDefault("ExceptionCode")) ?? 0;
        var module = data.GetValueOrDefault("ModuleName");
        return new AppCrash(
            time,
            app,
            string.IsNullOrWhiteSpace(module) ? app : module,
            NullIfBlank(data.GetValueOrDefault("ModulePath")),
            exception,
            NullIfBlank(data.GetValueOrDefault("AppPath")));
    }

    private static bool TryLoad(string xml, out XElement root)
    {
        root = null!;
        if (string.IsNullOrWhiteSpace(xml))
        {
            return false;
        }

        try
        {
            root = XDocument.Parse(xml).Root ?? throw new XmlException("Sem raiz.");
            return root.Name == EventNamespace + "Event";
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static DateTimeOffset? TimeOf(XElement root)
    {
        var stamp = root.Element(EventNamespace + "System")?.Element(EventNamespace + "TimeCreated")?.Attribute("SystemTime")?.Value;
        return DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) ? time : null;
    }

    private static Dictionary<string, string> DataOf(XElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = root.Element(EventNamespace + "EventData")?.Elements(EventNamespace + "Data") ?? [];
        foreach (var item in items)
        {
            if (item.Attribute("Name")?.Value is { Length: > 0 } name)
            {
                result[name] = item.Value;
            }
        }

        return result;
    }

    private static ulong? ParseNumber(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        }

        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static uint? ParseHex(string? text)
    {
        var value = text?.Trim();
        if (value is not null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        return uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
