using System.Text.RegularExpressions;

namespace M1vClock.Hardware.Devices;

public static partial class UsbDeviceParser
{
    private const int GenericScore = 9;
    private const int GenericInputScore = 5;

    public static IReadOnlyList<UsbDeviceInfo> Parse(IReadOnlyList<PnpEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var parsed = entries
            .Select(e => (Entry: e, Match: UsbId().Match(e.DeviceId)))
            .Where(p => p.Match.Success)
            .Select(p => new Node(p.Entry, p.Match.Groups["vid"].Value.ToUpperInvariant(), p.Match.Groups["pid"].Value.ToUpperInvariant(), p.Match.Groups["mi"].Success))
            .ToList();
        var children = parsed.Where(n => n.IsInterface).ToLookup(n => (n.Vid, n.Pid));
        return [.. parsed
            .Where(n => !n.IsInterface)
            .Select(top => Build(top, children[(top.Vid, top.Pid)].ToList()))
            .OrderBy(d => d.Category == UsbCategory.Hub)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static UsbCategory CategoryOf(string pnpClass, string name)
    {
        ArgumentNullException.ThrowIfNull(pnpClass);
        ArgumentNullException.ThrowIfNull(name);
        if (name.Contains("Hub", StringComparison.OrdinalIgnoreCase))
        {
            return UsbCategory.Hub;
        }

        if (name.Contains("Mass Storage", StringComparison.OrdinalIgnoreCase) || name.Contains("Armazenamento em massa", StringComparison.OrdinalIgnoreCase))
        {
            return UsbCategory.Storage;
        }

        return pnpClass.ToUpperInvariant() switch
        {
            "PORTS" or "MODEM" => UsbCategory.SerialPort,
            "MEDIA" or "AUDIOENDPOINT" => UsbCategory.Audio,
            "HIDCLASS" or "KEYBOARD" or "MOUSE" => UsbCategory.Input,
            "DISKDRIVE" or "WPD" or "SCSIADAPTER" => UsbCategory.Storage,
            "IMAGE" or "CAMERA" => UsbCategory.Imaging,
            "BLUETOOTH" => UsbCategory.Bluetooth,
            "NET" => UsbCategory.Network,
            _ => UsbCategory.Generic,
        };
    }

    private static UsbDeviceInfo Build(Node top, List<Node> children)
    {
        var group = new List<Node> { top };
        group.AddRange(children);
        var best = group.OrderBy(Score).ThenBy(n => n.IsInterface).First();
        var port = group.Select(n => ComPort().Match(n.Entry.Name)).FirstOrDefault(m => m.Success)?.Groups["port"].Value.ToUpperInvariant();
        var problem = group.Select(n => n.Entry.ProblemCode).FirstOrDefault(code => code > 0);
        return new UsbDeviceInfo(
            top.Entry.DeviceId,
            best.Entry.Name,
            best.Entry.PnpClass,
            CategoryOf(best.Entry.PnpClass, best.Entry.Name),
            top.Vid,
            top.Pid,
            UsbVendors.Name(top.Vid),
            port,
            problem > 0 ? problem : null);
    }

    private static int Score(Node node)
    {
        var name = node.Entry.Name;
        if (ComPort().IsMatch(name))
        {
            return 0;
        }

        if (name.Contains("Composite", StringComparison.OrdinalIgnoreCase) || name.Contains("Composto", StringComparison.OrdinalIgnoreCase) || name.Contains("Hub", StringComparison.OrdinalIgnoreCase))
        {
            return GenericScore;
        }

        return node.Entry.PnpClass.ToUpperInvariant() switch
        {
            "PORTS" => 1,
            "MEDIA" or "AUDIOENDPOINT" or "CAMERA" or "IMAGE" or "WPD" or "BLUETOOTH" or "NET" or "DISKDRIVE" => 2,
            "USBDEVICE" => 3,
            "HIDCLASS" when IsGenericInputName(name) => GenericInputScore,
            "HIDCLASS" => 4,
            _ => 6,
        };
    }

    private static bool IsGenericInputName(string name) =>
        name.Contains("Entrada USB", StringComparison.OrdinalIgnoreCase)
        || name.Contains("USB Input Device", StringComparison.OrdinalIgnoreCase)
        || name.Contains("HID-compliant", StringComparison.OrdinalIgnoreCase)
        || name.Contains("compatível com HID", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^USB\\VID_(?<vid>[0-9A-F]{4})&PID_(?<pid>[0-9A-F]{4})(?<mi>&MI_[0-9A-F]{2})?\\", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsbId();

    [GeneratedRegex(@"\((?<port>COM\d+)\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ComPort();

    private sealed record Node(PnpEntry Entry, string Vid, string Pid, bool IsInterface);
}
