namespace M1vClock.App.DesignSystem.Motion;

public readonly record struct SpringSpec(double DampingRatio, double Response)
{
    public static SpringSpec Standard { get; } = new(1.0, 0.30);

    public static SpringSpec Quick { get; } = new(1.0, 0.20);

    public static SpringSpec Gentle { get; } = new(1.0, 0.40);

    public static SpringSpec Press { get; } = new(1.0, 0.12);

    public static SpringSpec Momentum { get; } = new(0.8, 0.30);
}

public static class SpringMath
{
    public const double DefaultDecelerationRate = 0.998;

    public static (double Position, double Velocity) Step(double position, double velocity, double target, SpringSpec spec, double seconds)
    {
        if (seconds <= 0)
        {
            return (position, velocity);
        }

        var omega = 2 * Math.PI / Math.Max(spec.Response, 0.01);
        var zeta = Math.Clamp(spec.DampingRatio, 0.05, 1.0);
        var offset = position - target;

        if (zeta >= 0.9999)
        {
            var decay = Math.Exp(-omega * seconds);
            var slope = velocity + omega * offset;
            var x = (offset + slope * seconds) * decay;
            var v = (slope - omega * (offset + slope * seconds)) * decay;
            return (target + x, v);
        }

        var damped = omega * Math.Sqrt(1 - zeta * zeta);
        var envelope = Math.Exp(-zeta * omega * seconds);
        var b = (velocity + zeta * omega * offset) / damped;
        var cos = Math.Cos(damped * seconds);
        var sin = Math.Sin(damped * seconds);
        var oscillation = offset * cos + b * sin;
        var xt = envelope * oscillation;
        var vt = envelope * (-zeta * omega * oscillation + damped * (b * cos - offset * sin));
        return (target + xt, vt);
    }

    public static bool IsAtRest(double position, double velocity, double target, double precision) =>
        Math.Abs(position - target) < precision && Math.Abs(velocity) < precision * 10;

    public static double SettleTime(SpringSpec spec, double fraction)
    {
        var position = 1.0;
        var velocity = 0.0;
        var elapsed = 0.0;
        const double step = 1.0 / 1000;
        while (elapsed < 5)
        {
            (position, velocity) = Step(position, velocity, 0, spec, step);
            elapsed += step;
            if (Math.Abs(position) <= 1 - fraction && Math.Abs(velocity) < 0.5)
            {
                return elapsed;
            }
        }

        return elapsed;
    }

    public static double Project(double velocity, double decelerationRate = DefaultDecelerationRate) =>
        velocity / 1000 * decelerationRate / (1 - decelerationRate);

    public static double RubberBand(double overshoot, double dimension, double constant = 0.55) =>
        overshoot * dimension * constant / (dimension + constant * Math.Abs(overshoot));
}

public sealed class VelocityTracker
{
    private readonly Queue<(double Time, double Position)> _samples = new();

    public void Reset() => _samples.Clear();

    public void Add(double seconds, double position)
    {
        _samples.Enqueue((seconds, position));
        while (_samples.Count > 2 && seconds - _samples.Peek().Time > 0.1)
        {
            _samples.Dequeue();
        }
    }

    public double Velocity()
    {
        if (_samples.Count < 2)
        {
            return 0;
        }

        var first = _samples.Peek();
        var last = _samples.Last();
        var elapsed = last.Time - first.Time;
        return elapsed <= 0 ? 0 : (last.Position - first.Position) / elapsed;
    }
}
