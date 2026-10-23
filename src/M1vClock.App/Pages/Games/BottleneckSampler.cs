using System.Text.RegularExpressions;
using M1vClock.Games.Bottleneck;
using M1vClock.Hardware.Sensors;

namespace M1vClock.App.Pages.Games;

public static partial class BottleneckSampler
{
    [GeneratedRegex(@"^CPU Core #\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex CoreName();

    public static LoadSample From(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new LoadSample(
            snapshot.GpuLoad,
            snapshot.CpuLoad,
            BusiestCore(snapshot),
            VideoMemoryPercent(snapshot),
            snapshot.MemoryLoad,
            Positive(snapshot.GpuTemperature),
            Positive(snapshot.CpuTemperature));
    }

    public static float? BusiestCore(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var loads = snapshot.Readings.Where(r => r.Hardware == SensorHardware.Cpu && r.Kind == SensorKind.Load && r.Value is not null).ToList();
        var precomputed = loads.FirstOrDefault(r => r.Name == "CPU Core Max")?.Value;
        if (precomputed is not null)
        {
            return precomputed;
        }

        var cores = loads.Where(r => CoreName().IsMatch(r.Name)).Select(r => r.Value!.Value).ToList();
        return cores.Count > 0 ? cores.Max() : null;
    }

    public static double? VideoMemoryPercent(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Percent(snapshot, "GPU Memory Used", "GPU Memory Total") ?? Percent(snapshot, "D3D Dedicated Memory Used", "D3D Dedicated Memory Total");
    }

    private static double? Percent(SensorSnapshot snapshot, string usedName, string totalName)
    {
        var used = Gpu(snapshot, usedName);
        var total = Gpu(snapshot, totalName);
        return used is not null && total is > 0 ? Math.Min(100.0, 100.0 * used.Value / total.Value) : null;
    }

    private static float? Gpu(SensorSnapshot snapshot, string name) =>
        snapshot.Readings.FirstOrDefault(r => r.Hardware == SensorHardware.Gpu && r.Kind == SensorKind.SmallData && r.Name == name)?.Value;

    private static float? Positive(float? value) => value is > 0 ? value : null;
}
