namespace M1vClock.Benchmarks.Safety;

public enum ThermalComponent
{
    Cpu,
    Gpu,
}

public enum TripReason
{
    Temperature,
    MemoryErrors,
    SensorLost,
    WindowUnresponsive,
    UserRequested,
    ComponentFailed,
}

public sealed record ComponentSample(float? Temperature, float? Load, float? Power, float? Clock);

public interface IComponentProbe
{
    ComponentSample? Read(ThermalComponent component);
}

public sealed record SafetyTrip(TripReason Reason, ThermalComponent? Component, double Value, double Limit, TimeSpan At);

public sealed record SafetySample(TimeSpan At, ComponentSample? Cpu, ComponentSample? Gpu);

public sealed record SafetyLimits
{
    public const double CpuMinimum = 60;
    public const double CpuDefault = 85;
    public const double CpuConfirmAbove = 90;
    public const double CpuHardMaximum = 95;
    public const double GpuMinimum = 60;
    public const double GpuDefault = 83;
    public const double GpuConfirmAbove = 90;
    public const double GpuHardMaximum = 95;
    public const int SensorLostSamples = 3;
    public const int UnresponsiveSamples = 3;

    public static readonly TimeSpan CpuInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan GpuInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SlowestInterval = TimeSpan.FromSeconds(5);

    public SafetyLimits(double cpuCelsius, double gpuCelsius, TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cpuCelsius, CpuMinimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cpuCelsius, CpuHardMaximum);
        ArgumentOutOfRangeException.ThrowIfLessThan(gpuCelsius, GpuMinimum);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gpuCelsius, GpuHardMaximum);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.FromMilliseconds(250));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(interval, SlowestInterval);
        CpuCelsius = cpuCelsius;
        GpuCelsius = gpuCelsius;
        Interval = interval;
    }

    public double CpuCelsius { get; }

    public double GpuCelsius { get; }

    public TimeSpan Interval { get; }

    public static SafetyLimits Defaults(bool gpuTest) => new(CpuDefault, GpuDefault, gpuTest ? GpuInterval : CpuInterval);

    public static bool CpuNeedsConfirmation(double celsius) => celsius > CpuConfirmAbove;

    public static bool GpuNeedsConfirmation(double celsius) => celsius > GpuConfirmAbove;

    public double LimitOf(ThermalComponent component) => component == ThermalComponent.Cpu ? CpuCelsius : GpuCelsius;
}
