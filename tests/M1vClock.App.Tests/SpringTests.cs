using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.Tests;

public sealed class SpringTests
{
    private const double Frame = 1.0 / 60;

    public static TheoryData<string, double, double> Specs => new()
    {
        { "Padrão", SpringSpec.Standard.DampingRatio, SpringSpec.Standard.Response },
        { "Rápida", SpringSpec.Quick.DampingRatio, SpringSpec.Quick.Response },
        { "Suave", SpringSpec.Gentle.DampingRatio, SpringSpec.Gentle.Response },
        { "Toque", SpringSpec.Press.DampingRatio, SpringSpec.Press.Response },
        { "Impulso", SpringSpec.Momentum.DampingRatio, SpringSpec.Momentum.Response },
    };

    [Theory]
    [MemberData(nameof(Specs))]
    public void EverySpecConvergesToTheTarget(string name, double damping, double response)
    {
        var spec = new SpringSpec(damping, response);
        var (position, velocity) = (0.0, 0.0);

        for (var i = 0; i < 60 * 3; i++)
        {
            (position, velocity) = SpringMath.Step(position, velocity, 100, spec, Frame);
        }

        Assert.True(Math.Abs(position - 100) < 0.01, $"{name} terminou em {position}.");
        Assert.True(Math.Abs(velocity) < 0.1, $"{name} terminou com velocidade {velocity}.");
    }

    [Fact]
    public void CriticallyDampedSpringNeverOvershoots()
    {
        foreach (var spec in new[] { SpringSpec.Standard, SpringSpec.Quick, SpringSpec.Gentle, SpringSpec.Press })
        {
            var (position, velocity) = (0.0, 0.0);
            for (var i = 0; i < 300; i++)
            {
                (position, velocity) = SpringMath.Step(position, velocity, 1, spec, Frame);
                Assert.True(position <= 1 + 1e-9, $"passou do alvo: {position}");
            }
        }
    }

    [Fact]
    public void UnderdampedSpringOvershootsWhenReleasedWithVelocity()
    {
        var (position, velocity) = (0.0, 600.0);
        var peak = 0.0;

        for (var i = 0; i < 120; i++)
        {
            (position, velocity) = SpringMath.Step(position, velocity, 100, SpringSpec.Momentum, Frame);
            peak = Math.Max(peak, position);
        }

        Assert.True(peak > 100, $"pico {peak}");
    }

    [Fact]
    public void ZeroElapsedTimeKeepsTheState()
    {
        var result = SpringMath.Step(12, 34, 100, SpringSpec.Standard, 0);

        Assert.Equal((12.0, 34.0), result);
    }

    [Theory]
    [MemberData(nameof(Specs))]
    public void TwoHalfStepsEqualOneFullStep(string name, double damping, double response)
    {
        var spec = new SpringSpec(damping, response);

        var whole = SpringMath.Step(10, 250, 100, spec, 0.1);
        var half = SpringMath.Step(10, 250, 100, spec, 0.05);
        var twice = SpringMath.Step(half.Position, half.Velocity, 100, spec, 0.05);

        Assert.True(Math.Abs(whole.Position - twice.Position) < 1e-6, name);
        Assert.True(Math.Abs(whole.Velocity - twice.Velocity) < 1e-6, name);
    }

    [Fact]
    public void FasterResponseSettlesSooner()
    {
        var press = SpringMath.SettleTime(SpringSpec.Press, 0.95);
        var quick = SpringMath.SettleTime(SpringSpec.Quick, 0.95);
        var standard = SpringMath.SettleTime(SpringSpec.Standard, 0.95);
        var gentle = SpringMath.SettleTime(SpringSpec.Gentle, 0.95);

        Assert.True(press < quick && quick < standard && standard < gentle);
    }

    [Fact]
    public void StandardSpringSettlesWellUnderHalfASecond()
    {
        Assert.InRange(SpringMath.SettleTime(SpringSpec.Standard, 0.95), 0.1, 0.5);
    }

    [Fact]
    public void IsAtRestNeedsBothPositionAndVelocity()
    {
        Assert.True(SpringMath.IsAtRest(100.001, 0.001, 100, 0.01));
        Assert.False(SpringMath.IsAtRest(100.001, 5, 100, 0.01));
        Assert.False(SpringMath.IsAtRest(101, 0, 100, 0.01));
    }

    [Fact]
    public void ProjectionOfAThousandPixelsPerSecondMatchesTheScrollFormula()
    {
        Assert.Equal(499.0, SpringMath.Project(1000), 6);
    }

    [Fact]
    public void ProjectionKeepsTheSignAndIsZeroAtRest()
    {
        Assert.Equal(0.0, SpringMath.Project(0));
        Assert.Equal(-SpringMath.Project(800), SpringMath.Project(-800), 9);
    }

    [Fact]
    public void RubberBandResistsMoreTheFartherItIsPulled()
    {
        var near = SpringMath.RubberBand(20, 400);
        var far = SpringMath.RubberBand(200, 400);

        Assert.InRange(near, 0, 20);
        Assert.InRange(far, near, 200);
        Assert.True(far / 200 < near / 20);
    }

    [Fact]
    public void RubberBandNeverExceedsTheDimension()
    {
        Assert.True(SpringMath.RubberBand(1_000_000, 400) < 400 / 0.55 + 1);
        Assert.Equal(0.0, SpringMath.RubberBand(0, 400));
        Assert.Equal(-SpringMath.RubberBand(50, 400), SpringMath.RubberBand(-50, 400), 9);
    }

    [Fact]
    public void VelocityTrackerMeasuresConstantSpeed()
    {
        var tracker = new VelocityTracker();

        for (var i = 0; i <= 5; i++)
        {
            tracker.Add(i * 0.016, i * 1.6);
        }

        Assert.Equal(100.0, tracker.Velocity(), 6);
    }

    [Fact]
    public void VelocityTrackerNeedsTwoSamples()
    {
        var tracker = new VelocityTracker();
        Assert.Equal(0.0, tracker.Velocity());

        tracker.Add(0, 10);
        Assert.Equal(0.0, tracker.Velocity());
    }

    [Fact]
    public void VelocityTrackerForgetsOldMovement()
    {
        var tracker = new VelocityTracker();
        tracker.Add(0, 0);
        tracker.Add(0.01, 100);
        tracker.Add(1.0, 100);
        tracker.Add(1.016, 100);

        Assert.Equal(0.0, tracker.Velocity(), 6);
    }

    [Fact]
    public void VelocityTrackerResetClearsSamples()
    {
        var tracker = new VelocityTracker();
        tracker.Add(0, 0);
        tracker.Add(0.016, 10);

        tracker.Reset();

        Assert.Equal(0.0, tracker.Velocity());
    }
}
