namespace M1vClock.Games.Overlay;

public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public enum OverlayGroup
{
    Frames,
    Gpu,
    Vram,
    Cpu,
    Ram,
    Other,
}

public sealed record OverlayMetric(string Key, OverlayGroup Group, bool IsDefault);

public static class OverlayMetrics
{
    public const string Fps = "fps";
    public const string FrameTime = "frametime";
    public const string Low1 = "low1";
    public const string Graph = "graph";
    public const string GpuLoad = "gpu-load";
    public const string GpuTemperature = "gpu-temp";
    public const string GpuHotSpot = "gpu-hotspot";
    public const string GpuClock = "gpu-clock";
    public const string GpuMemoryClock = "gpu-memclock";
    public const string GpuPower = "gpu-power";
    public const string GpuFan = "gpu-fan";
    public const string VramUsed = "vram-used";
    public const string VramPercent = "vram-pct";
    public const string CpuLoad = "cpu-load";
    public const string CpuMaxCore = "cpu-maxcore";
    public const string CpuTemperature = "cpu-temp";
    public const string CpuClock = "cpu-clock";
    public const string CpuPower = "cpu-power";
    public const string RamUsed = "ram-used";
    public const string RamPercent = "ram-pct";
    public const string GameName = "game";
    public const string Time = "clock";

    public static IReadOnlyList<OverlayMetric> All { get; } =
    [
        new(Fps, OverlayGroup.Frames, true),
        new(FrameTime, OverlayGroup.Frames, true),
        new(Low1, OverlayGroup.Frames, true),
        new(Graph, OverlayGroup.Frames, true),
        new(GpuLoad, OverlayGroup.Gpu, true),
        new(GpuTemperature, OverlayGroup.Gpu, true),
        new(GpuHotSpot, OverlayGroup.Gpu, false),
        new(GpuClock, OverlayGroup.Gpu, true),
        new(GpuMemoryClock, OverlayGroup.Gpu, false),
        new(GpuPower, OverlayGroup.Gpu, true),
        new(GpuFan, OverlayGroup.Gpu, false),
        new(VramUsed, OverlayGroup.Vram, true),
        new(VramPercent, OverlayGroup.Vram, false),
        new(CpuLoad, OverlayGroup.Cpu, true),
        new(CpuMaxCore, OverlayGroup.Cpu, false),
        new(CpuTemperature, OverlayGroup.Cpu, true),
        new(CpuClock, OverlayGroup.Cpu, true),
        new(CpuPower, OverlayGroup.Cpu, true),
        new(RamUsed, OverlayGroup.Ram, true),
        new(RamPercent, OverlayGroup.Ram, false),
        new(GameName, OverlayGroup.Other, true),
        new(Time, OverlayGroup.Other, false),
    ];

    public static IReadOnlyList<string> Defaults() => All.Where(m => m.IsDefault).Select(m => m.Key).ToList();

    public static IReadOnlyList<string> Minimal() => [Fps, FrameTime, Low1];

    public static IReadOnlyList<string> Everything() => All.Select(m => m.Key).ToList();

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? keys)
    {
        if (keys is null)
        {
            return Defaults();
        }

        var chosen = keys.ToHashSet(StringComparer.Ordinal);
        return All.Select(m => m.Key).Where(chosen.Contains).ToList();
    }
}

public sealed class OverlaySettings
{
    public const double MinimumScale = 0.75;
    public const double MaximumScale = 2.0;

    public OverlayCorner Corner { get; set; } = OverlayCorner.TopLeft;

    public double Scale { get; set; } = 1.0;

    public List<string>? Items { get; set; }

    public string Hotkey { get; set; } = "Ctrl+Shift+F10";
}
