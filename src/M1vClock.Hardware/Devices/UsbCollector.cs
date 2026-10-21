using M1vClock.Hardware.Wmi;

namespace M1vClock.Hardware.Devices;

public sealed class UsbCollector(IWmiReader wmi)
{
    private static readonly string[] Properties = ["DeviceID", "Name", "PNPClass", "ConfigManagerErrorCode"];

    public IReadOnlyList<UsbDeviceInfo> ReadDevices()
    {
        var rows = wmi.Query(new WmiQuery("Win32_PnPEntity", Properties));
        var entries = rows
            .Select(row => (Id: row.Text("DeviceID"), Row: row))
            .Where(p => p.Id is not null)
            .Select(p => new PnpEntry(p.Id!, p.Row.Text("Name") ?? p.Id!, p.Row.Text("PNPClass") ?? string.Empty, (uint)Math.Max(0, p.Row.Number("ConfigManagerErrorCode"))))
            .ToList();
        return UsbDeviceParser.Parse(entries);
    }
}
