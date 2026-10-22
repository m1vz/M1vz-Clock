using M1vClock.Benchmarks.Cpu;

namespace M1vClock.Benchmarks.Tests;

public sealed class GameSimulationTests
{
    private const int Entities = 3000;

    private static GameSimulation Advanced(int frames)
    {
        var simulation = new GameSimulation(Entities);
        for (var i = 0; i < frames; i++)
        {
            simulation.AdvanceFrame(out _, out _);
        }

        return simulation;
    }

    [Fact]
    public void TheDefaultKeepsThirtyThousandUnits()
    {
        Assert.Equal(30_000, new GameSimulation().Entities);
    }

    [Fact]
    public void TwoSimulationsDoTheSameThingFrameByFrame()
    {
        Assert.Equal(Advanced(40).Checksum(), Advanced(40).Checksum());
    }

    [Fact]
    public void MoreFramesChangeTheState()
    {
        Assert.NotEqual(Advanced(10).Checksum(), Advanced(40).Checksum());
    }

    [Fact]
    public void ResetReturnsToTheSameStartingState()
    {
        var simulation = new GameSimulation(Entities);
        var start = simulation.Checksum();
        for (var i = 0; i < 20; i++)
        {
            simulation.AdvanceFrame(out _, out _);
        }

        simulation.Reset();

        Assert.Equal(start, simulation.Checksum());
    }

    [Fact]
    public void UnitsNeverLeaveTheWorldAndBothTeamsStayPresent()
    {
        var simulation = Advanced(120);
        var x = new float[Entities];
        var y = new float[Entities];
        var team = new byte[Entities];

        simulation.CopyView(x, y, team);

        Assert.All(x, v => Assert.InRange(v, 0f, GameSimulation.World));
        Assert.All(y, v => Assert.InRange(v, 0f, GameSimulation.World));
        Assert.Equal(Entities / 2, team.Count(t => t == 0));
        Assert.Equal(Entities / 2, team.Count(t => t == 1));
    }

    [Fact]
    public void TheBattleKeepsMovingInsteadOfFreezing()
    {
        var simulation = new GameSimulation(Entities);
        var before = new float[Entities];
        var after = new float[Entities];
        var ignoredY = new float[Entities];
        var ignoredTeam = new byte[Entities];
        simulation.CopyView(before, ignoredY, ignoredTeam);

        for (var i = 0; i < 60; i++)
        {
            simulation.AdvanceFrame(out _, out _);
        }

        simulation.CopyView(after, ignoredY, ignoredTeam);
        Assert.True(before.Zip(after).Count(pair => Math.Abs(pair.First - pair.Second) > 1f) > Entities / 2);
    }

    [Fact]
    public void ARunReportsFramesTimesAndFps()
    {
        var simulation = new GameSimulation(Entities);

        var result = simulation.Run(TimeSpan.FromMilliseconds(400), TimeSpan.Zero, TestContext.Current.CancellationToken);

        Assert.True(result.Frames > 5);
        Assert.True(result.AverageFps > 0);
        Assert.True(result.LowFps > 0);
        Assert.True(result.MainThreadMilliseconds > 0);
        Assert.True(result.ParallelMilliseconds > 0);
        Assert.Equal(result.AverageFps, result.Score);
        Assert.Equal(result.Frames, simulation.FramesDone);
        Assert.True(result.Seconds >= 0.4);
    }

    [Fact]
    public void CancellingStopsTheRunWithACancellation()
    {
        var simulation = new GameSimulation(Entities);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        Assert.ThrowsAny<OperationCanceledException>(() => simulation.Run(TimeSpan.FromSeconds(30), TimeSpan.Zero, cancellation.Token));
    }

    [Fact]
    public void ACopyIntoASmallBufferIsRejected()
    {
        var simulation = new GameSimulation(Entities);
        var y = new float[Entities];
        var team = new byte[Entities];

        Assert.Throws<ArgumentOutOfRangeException>(() => simulation.CopyView(new float[10], y, team));
    }

    [Fact]
    public void SomeUnitsAreVisibleToTheCameraAfterAFrame()
    {
        var simulation = Advanced(2);

        Assert.InRange(simulation.VisibleEntities, 1, Entities);
    }
}
