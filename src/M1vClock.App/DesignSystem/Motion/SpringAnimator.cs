using System.Windows;
using System.Windows.Media;

namespace M1vClock.App.DesignSystem.Motion;

public static class SpringAnimator
{
    private const double MaxFrameSeconds = 1.0 / 20;

    private static readonly Dictionary<(DependencyObject Target, DependencyProperty Property), ActiveSpring> Active = [];
    private static TimeSpan _lastFrame;
    private static bool _hooked;

    public static bool ReduceMotion { get; set; }

    public static void Animate(
        DependencyObject target,
        DependencyProperty property,
        double to,
        SpringSpec spec,
        double? initialVelocity = null,
        double precision = 0.01,
        Action? completed = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        var key = (target, property);
        if (ReduceMotion && property != UIElement.OpacityProperty)
        {
            Active.Remove(key);
            target.SetValue(property, to);
            completed?.Invoke();
            return;
        }

        var effectiveSpec = ReduceMotion ? SpringSpec.Quick : spec;
        if (Active.TryGetValue(key, out var spring))
        {
            spring.Target = to;
            spring.Spec = effectiveSpec;
            spring.Precision = precision;
            if (initialVelocity is double velocity)
            {
                spring.Velocity = velocity;
            }

            spring.Completed = completed;
            return;
        }

        var current = (double)target.GetValue(property);
        if (Math.Abs(current - to) < precision && (initialVelocity ?? 0) == 0)
        {
            target.SetValue(property, to);
            completed?.Invoke();
            return;
        }

        Active[key] = new ActiveSpring
        {
            Position = current,
            Velocity = initialVelocity ?? 0,
            Target = to,
            Spec = effectiveSpec,
            Precision = precision,
            Completed = completed,
        };
        Hook();
    }

    public static void Set(DependencyObject target, DependencyProperty property, double value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        Active.Remove((target, property));
        target.SetValue(property, value);
    }

    public static double VelocityOf(DependencyObject target, DependencyProperty property) =>
        Active.TryGetValue((target, property), out var spring) ? spring.Velocity : 0;

    public static bool IsAnimating(DependencyObject target, DependencyProperty property) =>
        Active.ContainsKey((target, property));

    private static void Hook()
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private static void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        _hooked = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs rendering ? rendering.RenderingTime : TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
        if (now == _lastFrame)
        {
            return;
        }

        var seconds = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : Math.Clamp((now - _lastFrame).TotalSeconds, 0, MaxFrameSeconds);
        _lastFrame = now;

        List<Action>? finished = null;
        foreach (var (key, spring) in Active.ToList())
        {
            (spring.Position, spring.Velocity) = SpringMath.Step(spring.Position, spring.Velocity, spring.Target, spring.Spec, seconds);
            if (SpringMath.IsAtRest(spring.Position, spring.Velocity, spring.Target, spring.Precision))
            {
                key.Target.SetValue(key.Property, spring.Target);
                Active.Remove(key);
                if (spring.Completed is not null)
                {
                    (finished ??= []).Add(spring.Completed);
                }
            }
            else
            {
                key.Target.SetValue(key.Property, spring.Position);
            }
        }

        if (Active.Count == 0)
        {
            Unhook();
        }

        if (finished is not null)
        {
            foreach (var callback in finished)
            {
                callback();
            }
        }
    }

    private sealed class ActiveSpring
    {
        public double Position { get; set; }

        public double Velocity { get; set; }

        public double Target { get; set; }

        public SpringSpec Spec { get; set; }

        public double Precision { get; set; }

        public Action? Completed { get; set; }
    }
}
