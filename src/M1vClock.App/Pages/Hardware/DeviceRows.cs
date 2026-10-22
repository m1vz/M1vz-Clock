using M1vClock.App.Localization;
using M1vClock.App.Services;
using M1vClock.Hardware.Devices;

namespace M1vClock.App.Pages.Hardware;

public static class DeviceRows
{
    private static readonly uint[] KnownProblemCodes = [10, 22, 28, 31, 43];

    public static string CategoryText(UsbCategory category) => Loc.Get($"Hw.Dev.Category.{category}");

    public static string Detail(UsbDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var parts = new[] { CategoryText(device.Category), device.Maker, device.ComPort, device.HardwareId };
        return string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    public static string ProblemText(uint code) =>
        KnownProblemCodes.Contains(code)
            ? Loc.Get($"Hw.Dev.Problem.Code.{code}")
            : Loc.Format("Hw.Dev.Problem.Code.Other", code.ToString(Loc.Culture));

    public static IReadOnlyList<InfoRowViewModel> Connected(IReadOnlyList<UsbDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return [.. devices.Where(d => !d.HasProblem).Select(d => new InfoRowViewModel(d.Name, Detail(d)))];
    }

    public static IReadOnlyList<InfoRowViewModel> WithProblem(IReadOnlyList<UsbDeviceInfo> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        return [.. devices.Where(d => d.HasProblem).Select(d => new InfoRowViewModel(d.Name, ProblemText(d.ProblemCode!.Value)))];
    }

    public static IReadOnlyList<InfoRowViewModel> Activity(IReadOnlyList<ActivityEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return [.. entries.Select(e => new InfoRowViewModel(e.At.LocalDateTime.ToString("T", Loc.Culture), Loc.Format("Hw.Dev.Activity.Entry", e.Title, e.Text)))];
    }
}
