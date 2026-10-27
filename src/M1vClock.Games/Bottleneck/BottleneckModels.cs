namespace M1vClock.Games.Bottleneck;

public enum Limiter
{
    Gpu,
    Cpu,
    Vram,
    Ram,
    Thermal,
    Capped,
}

public readonly record struct LimiterShare(Limiter Limiter, double Percent);

public sealed record LoadSample(
    float? GpuLoad,
    float? CpuTotal,
    float? CpuBusiestCore,
    double? VramPercent,
    float? RamPercent,
    float? GpuTemperature,
    float? CpuTemperature,
    bool GpuThermalCap = false,
    double? Fps = null,
    double? FpsLow = null)
{
    public const double VramFull = 97;
    public const double CpuTemperatureLimit = 95;
    public const double GpuTemperatureLimit = 92;
    public const double GpuBound = 90;
    public const double CpuBound = 85;
    public const double RamFull = 92;

    public Limiter Classify()
    {
        if (VramPercent >= VramFull)
        {
            return Limiter.Vram;
        }

        if (GpuThermalCap || CpuTemperature >= CpuTemperatureLimit || GpuTemperature >= GpuTemperatureLimit)
        {
            return Limiter.Thermal;
        }

        if (GpuLoad >= GpuBound)
        {
            return Limiter.Gpu;
        }

        if (CpuBusiestCore >= CpuBound || CpuTotal >= CpuBound)
        {
            return Limiter.Cpu;
        }

        return RamPercent >= RamFull ? Limiter.Ram : Limiter.Capped;
    }
}

public sealed record SessionReport(
    int Samples,
    Limiter? Main,
    IReadOnlyList<LimiterShare> Distribution,
    double? AverageGpuLoad,
    double? AverageCpuTotal,
    double? PeakCpuCore,
    double? PeakVram,
    double? PeakRam,
    double? PeakGpuTemperature,
    double? PeakCpuTemperature,
    double? AverageFps,
    double? AverageFpsLow);

public sealed class BottleneckSession
{
    private readonly List<LoadSample> _samples = [];
    private readonly List<Limiter> _limiters = [];

    public BottleneckSession(DateTimeOffset start, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "A sessão precisa durar mais que zero.");
        }

        Start = start;
        Duration = duration;
    }

    public DateTimeOffset Start { get; }

    public TimeSpan Duration { get; }

    public int Count => _samples.Count;

    public bool IsDone(DateTimeOffset now) => now - Start >= Duration;

    public double Progress(DateTimeOffset now) => Math.Clamp((now - Start) / Duration, 0, 1);

    public void Add(LoadSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        _samples.Add(sample);
        _limiters.Add(sample.Classify());
    }

    public IReadOnlyList<LimiterShare> Distribution() =>
        _limiters
            .GroupBy(l => l)
            .Select(g => new LimiterShare(g.Key, 100.0 * g.Count() / _limiters.Count))
            .OrderByDescending(s => s.Percent)
            .ThenBy(s => s.Limiter)
            .ToList();

    public SessionReport Report() => new(
        Count,
        Count == 0 ? null : Distribution()[0].Limiter,
        Distribution(),
        Average(s => s.GpuLoad),
        Average(s => s.CpuTotal),
        Peak(s => s.CpuBusiestCore),
        Peak(s => s.VramPercent),
        Peak(s => s.RamPercent),
        Peak(s => s.GpuTemperature),
        Peak(s => s.CpuTemperature),
        Average(s => s.Fps is > 0 ? s.Fps : null),
        Average(s => s.FpsLow is > 0 ? s.FpsLow : null));

    private double? Average(Func<LoadSample, double?> select)
    {
        var values = _samples.Select(select).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count > 0 ? values.Average() : null;
    }

    private double? Peak(Func<LoadSample, double?> select)
    {
        var values = _samples.Select(select).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count > 0 ? values.Max() : null;
    }
}
