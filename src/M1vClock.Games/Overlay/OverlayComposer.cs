using System.Globalization;
using M1vClock.Games.Frames;

namespace M1vClock.Games.Overlay;

public sealed record OverlaySensors(
    float? GpuLoad = null,
    float? GpuTemperature = null,
    float? GpuHotSpot = null,
    float? GpuClock = null,
    float? GpuMemoryClock = null,
    float? GpuPower = null,
    float? GpuFanRpm = null,
    float? GpuFanPercent = null,
    float? VramUsedMb = null,
    float? VramTotalMb = null,
    float? CpuLoad = null,
    float? CpuMaxCore = null,
    float? CpuTemperature = null,
    float? CpuClockMhz = null,
    float? CpuPower = null,
    float? RamUsedGb = null,
    float? RamLoad = null);

public sealed record OverlayLine(string Label, string Value);

public sealed record OverlayTexts(string Waiting, string Core, string Memory, string Fan);

public sealed record OverlayView(
    bool ShowFps,
    bool ShowFrameTime,
    bool ShowLow,
    bool ShowGraph,
    string FpsText,
    string FrameTimeText,
    string LowText,
    IReadOnlyList<double> Graph,
    IReadOnlyList<OverlayLine> Lines,
    string Footer)
{
    public bool HasFooter => Footer.Length > 0;
}

public static class OverlayComposer
{
    private const string Missing = "—";

    public static OverlayView Compose(IReadOnlyCollection<string> items, FrameStats? frames, OverlaySensors? sensors, DateTimeOffset now, OverlayTexts texts, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(culture);
        var show = items.ToHashSet(StringComparer.Ordinal);
        var s = sensors ?? new OverlaySensors();
        string Number(double value, string format) => value.ToString(format, culture);
        string Temperature(float? value) => value is > 0 ? Number(value.Value, "0") + " °C" : Missing;
        string Percent(double? value) => value is { } v ? Number(v, "0") + "%" : Missing;

        var lines = new List<OverlayLine>();
        void Line(string label, List<string> parts)
        {
            if (parts.Count > 0)
            {
                lines.Add(new OverlayLine(label, string.Join("  ", parts)));
            }
        }

        var gpu = new List<string>();
        if (show.Contains(OverlayMetrics.GpuLoad))
        {
            gpu.Add(Percent(s.GpuLoad));
        }

        if (show.Contains(OverlayMetrics.GpuTemperature))
        {
            gpu.Add(Temperature(s.GpuTemperature));
        }

        if (show.Contains(OverlayMetrics.GpuHotSpot))
        {
            gpu.Add("HS " + Temperature(s.GpuHotSpot));
        }

        if (show.Contains(OverlayMetrics.GpuClock))
        {
            gpu.Add(s.GpuClock is > 0 ? Number(s.GpuClock.Value, "0") + " MHz" : Missing + " MHz");
        }

        if (show.Contains(OverlayMetrics.GpuMemoryClock))
        {
            gpu.Add(texts.Memory + " " + (s.GpuMemoryClock is > 0 ? Number(s.GpuMemoryClock.Value, "0") + " MHz" : Missing));
        }

        if (show.Contains(OverlayMetrics.GpuPower))
        {
            gpu.Add(s.GpuPower is > 0 ? Number(s.GpuPower.Value, "0") + " W" : Missing + " W");
        }

        if (show.Contains(OverlayMetrics.GpuFan))
        {
            gpu.Add(s.GpuFanRpm is > 0 ? Number(s.GpuFanRpm.Value, "0") + " RPM"
                : s.GpuFanPercent is { } fan ? texts.Fan + " " + Number(fan, "0") + "%"
                : texts.Fan + " " + Missing);
        }

        Line("GPU", gpu);

        var vram = new List<string>();
        if (show.Contains(OverlayMetrics.VramUsed))
        {
            vram.Add(s.VramUsedMb is >= 0 ? Number(s.VramUsedMb.Value / 1024.0, "0.0") + " GB" : Missing);
        }

        if (show.Contains(OverlayMetrics.VramPercent))
        {
            vram.Add(Percent(s.VramUsedMb is >= 0 && s.VramTotalMb is > 0 ? Math.Min(100, 100.0 * s.VramUsedMb.Value / s.VramTotalMb.Value) : null));
        }

        Line("VRAM", vram);

        var cpu = new List<string>();
        if (show.Contains(OverlayMetrics.CpuLoad))
        {
            cpu.Add(Percent(s.CpuLoad));
        }

        if (show.Contains(OverlayMetrics.CpuMaxCore))
        {
            cpu.Add(texts.Core + " " + Percent(s.CpuMaxCore));
        }

        if (show.Contains(OverlayMetrics.CpuTemperature))
        {
            cpu.Add(Temperature(s.CpuTemperature));
        }

        if (show.Contains(OverlayMetrics.CpuClock))
        {
            cpu.Add(s.CpuClockMhz is > 0 ? Number(s.CpuClockMhz.Value / 1000.0, "0.00") + " GHz" : Missing + " GHz");
        }

        if (show.Contains(OverlayMetrics.CpuPower))
        {
            cpu.Add(s.CpuPower is > 0 ? Number(s.CpuPower.Value, "0") + " W" : Missing + " W");
        }

        Line("CPU", cpu);

        var ram = new List<string>();
        if (show.Contains(OverlayMetrics.RamUsed))
        {
            ram.Add(s.RamUsedGb is >= 0 ? Number(s.RamUsedGb.Value, "0.0") + " GB" : Missing);
        }

        if (show.Contains(OverlayMetrics.RamPercent))
        {
            ram.Add(Percent(s.RamLoad));
        }

        Line("RAM", ram);

        var footer = new List<string>();
        if (show.Contains(OverlayMetrics.GameName) && frames is not null)
        {
            footer.Add(frames.ProcessName);
        }

        if (show.Contains(OverlayMetrics.Time))
        {
            footer.Add(now.ToString("t", culture));
        }

        var showFps = show.Contains(OverlayMetrics.Fps);
        var showFrameTime = show.Contains(OverlayMetrics.FrameTime);
        return new OverlayView(
            showFps,
            showFrameTime,
            show.Contains(OverlayMetrics.Low1),
            show.Contains(OverlayMetrics.Graph),
            frames is null ? Missing : Number(frames.Fps, "0"),
            frames is null ? string.Empty : Number(frames.FrameTimeMs, "0.0") + " ms",
            frames is null ? texts.Waiting : "1% low " + Number(frames.OnePercentLowFps, "0"),
            Graph(frames?.RecentFrameTimes),
            lines,
            string.Join("  ·  ", footer));
    }

    public static IReadOnlyList<double> Graph(IReadOnlyList<double>? frameTimes)
    {
        if (frameTimes is not { Count: >= 2 })
        {
            return [];
        }

        var scale = Math.Max(frameTimes.Max(), frameTimes.Average() * 2);
        return scale <= 0 ? frameTimes.Select(_ => 0.0).ToList() : frameTimes.Select(t => Math.Clamp(t / scale, 0, 1)).ToList();
    }
}
