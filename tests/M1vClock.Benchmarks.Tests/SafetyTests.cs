using M1vClock.Benchmarks.Safety;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Benchmarks.Tests;

internal sealed class FakeProbe : IComponentProbe
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ThermalComponent, Queue<ComponentSample?>> _script = [];
    private readonly Dictionary<ThermalComponent, ComponentSample?> _steady = [];

    public Exception? Failure { get; set; }

    public int Reads { get; private set; }

    public FakeProbe Steady(ThermalComponent component, float? temperature)
    {
        lock (_gate)
        {
            _steady[component] = temperature is null ? null : new ComponentSample(temperature, 50, 100, 4000);
        }

        return this;
    }

    public FakeProbe Then(ThermalComponent component, params float?[] temperatures)
    {
        lock (_gate)
        {
            if (!_script.TryGetValue(component, out var queue))
            {
                queue = new Queue<ComponentSample?>();
                _script[component] = queue;
            }

            foreach (var temperature in temperatures)
            {
                queue.Enqueue(temperature is null ? null : new ComponentSample(temperature, 50, 100, 4000));
            }
        }

        return this;
    }

    public ComponentSample? Read(ThermalComponent component)
    {
        lock (_gate)
        {
            Reads++;
            if (Failure is not null)
            {
                throw Failure;
            }

            if (_script.TryGetValue(component, out var queue) && queue.Count > 0)
            {
                return queue.Dequeue();
            }

            return _steady.GetValueOrDefault(component);
        }
    }
}

public sealed class SafetyLimitsTests
{
    [Theory]
    [InlineData(59.9)]
    [InlineData(95.1)]
    public void ACpuLimitOutsideTheLockedRangeIsRefused(double celsius)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SafetyLimits(celsius, 80, TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(59.9)]
    [InlineData(95.1)]
    public void AGpuLimitOutsideTheLockedRangeIsRefused(double celsius)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SafetyLimits(80, celsius, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TheLimitsAtTheEdgesOfTheRangeAreAccepted()
    {
        var low = new SafetyLimits(SafetyLimits.CpuMinimum, SafetyLimits.GpuMinimum, SafetyLimits.CpuInterval);
        var high = new SafetyLimits(SafetyLimits.CpuHardMaximum, SafetyLimits.GpuHardMaximum, SafetyLimits.SlowestInterval);

        Assert.Equal(60, low.CpuCelsius);
        Assert.Equal(95, high.GpuCelsius);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(5001)]
    public void TheReadingIntervalCannotBeAbsurdlyFastOrSlowerThanFiveSeconds(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SafetyLimits(80, 80, TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void ADefaultGpuTestReadsEveryTwoSecondsAndACpuTestEverySecond()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), SafetyLimits.Defaults(gpuTest: true).Interval);
        Assert.Equal(TimeSpan.FromSeconds(1), SafetyLimits.Defaults(gpuTest: false).Interval);
        Assert.Equal(SafetyLimits.CpuDefault, SafetyLimits.Defaults(false).CpuCelsius);
        Assert.Equal(SafetyLimits.GpuDefault, SafetyLimits.Defaults(true).GpuCelsius);
    }

    [Fact]
    public void AboveNinetyDegreesNeedsAnExtraConfirmationButNinetyItselfDoesNot()
    {
        Assert.False(SafetyLimits.CpuNeedsConfirmation(90));
        Assert.True(SafetyLimits.CpuNeedsConfirmation(90.5));
        Assert.False(SafetyLimits.GpuNeedsConfirmation(90));
        Assert.True(SafetyLimits.GpuNeedsConfirmation(91));
    }

    [Fact]
    public void EachComponentHasItsOwnLimit()
    {
        var limits = new SafetyLimits(85, 70, TimeSpan.FromSeconds(1));

        Assert.Equal(85, limits.LimitOf(ThermalComponent.Cpu));
        Assert.Equal(70, limits.LimitOf(ThermalComponent.Gpu));
    }
}

public sealed class SafetyMonitorTests : IAsyncDisposable
{
    private static readonly SafetyLimits Limits = new(85, 80, TimeSpan.FromSeconds(1));

    private readonly FakeTimeProvider _clock = new(Samples.Now);
    private readonly CollectingLogger<SafetyMonitor> _logger = new();
    private readonly FakeProbe _probe = new();
    private readonly List<SafetyTrip> _trips = [];
    private readonly SafetyMonitor _monitor;

    public SafetyMonitorTests()
    {
        _monitor = new SafetyMonitor(_probe, _clock, _logger);
        _monitor.Tripped += (_, trip) =>
        {
            lock (_trips)
            {
                _trips.Add(trip);
            }
        };
    }

    public async ValueTask DisposeAsync() => await _monitor.DisposeAsync();

    private IReadOnlyList<SafetyTrip> Trips
    {
        get
        {
            lock (_trips)
            {
                return [.. _trips];
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "A condição não chegou a tempo.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private async Task TickAsync(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            var before = _probe.Reads;
            _clock.Advance(Limits.Interval);
            await WaitUntilAsync(() => _probe.Reads > before);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private async Task StartAsync(IReadOnlyCollection<ThermalComponent> watched, Func<long>? errors = null, Func<bool>? responsive = null)
    {
        _monitor.Start(watched, Limits, errors, responsive);
        await WaitUntilAsync(() => _monitor.Latest is not null);
    }

    [Fact]
    public async Task AColdComponentNeverTripsAndItsMaximumIsRemembered()
    {
        _probe.Steady(ThermalComponent.Cpu, 50).Then(ThermalComponent.Cpu, 50, 61, 58);
        await StartAsync([ThermalComponent.Cpu]);

        await TickAsync(2);

        Assert.Empty(Trips);
        Assert.Equal(61, _monitor.MaximumOf(ThermalComponent.Cpu));
        Assert.Null(_monitor.MaximumOf(ThermalComponent.Gpu));
    }

    [Fact]
    public async Task ReachingTheLimitTripsRightAwayWithTheValueAndTheLimit()
    {
        _probe.Then(ThermalComponent.Cpu, 70, 86);
        await StartAsync([ThermalComponent.Cpu]);
        Assert.Empty(Trips);

        await TickAsync();

        var trip = Assert.Single(Trips);
        Assert.Equal(TripReason.Temperature, trip.Reason);
        Assert.Equal(ThermalComponent.Cpu, trip.Component);
        Assert.Equal(86, trip.Value);
        Assert.Equal(85, trip.Limit);
        Assert.Equal(trip, _monitor.Trip);
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 4201);
    }

    [Fact]
    public async Task ExactlyTheLimitCountsAsReached()
    {
        _probe.Then(ThermalComponent.Gpu, 80);

        await StartAsync([ThermalComponent.Gpu]);

        Assert.Equal(TripReason.Temperature, Assert.Single(Trips).Reason);
    }

    [Fact]
    public async Task EachComponentIsJudgedAgainstItsOwnLimit()
    {
        _probe.Steady(ThermalComponent.Cpu, 84).Steady(ThermalComponent.Gpu, 79);
        await StartAsync([ThermalComponent.Cpu, ThermalComponent.Gpu]);
        Assert.Empty(Trips);

        _probe.Then(ThermalComponent.Gpu, 81);
        await TickAsync();

        var trip = Assert.Single(Trips);
        Assert.Equal(ThermalComponent.Gpu, trip.Component);
    }

    [Fact]
    public async Task ThreeReadingsInARowWithoutTemperatureMeanTheSensorIsLost()
    {
        _probe.Then(ThermalComponent.Cpu, null, null, null);
        _monitor.Start([ThermalComponent.Cpu], Limits);

        await WaitUntilAsync(() => _monitor.Latest is not null);
        await TickAsync(2);

        var trip = Assert.Single(Trips);
        Assert.Equal(TripReason.SensorLost, trip.Reason);
        Assert.Equal(SafetyLimits.SensorLostSamples, trip.Limit);
    }

    [Fact]
    public async Task ASensorThatComesBackBeforeThreeMissesDoesNotTrip()
    {
        _probe.Then(ThermalComponent.Cpu, null, null, 60, null, null, 61).Steady(ThermalComponent.Cpu, 62);
        await StartAsync([ThermalComponent.Cpu]);

        await TickAsync(5);

        Assert.Empty(Trips);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    public async Task ZeroNegativeOrNotANumberCountsAsNoReading(float value)
    {
        _probe.Steady(ThermalComponent.Cpu, value);
        await StartAsync([ThermalComponent.Cpu]);

        await TickAsync(2);

        Assert.Equal(TripReason.SensorLost, Assert.Single(Trips).Reason);
        Assert.Null(_monitor.MaximumOf(ThermalComponent.Cpu));
    }

    [Fact]
    public async Task AProbeThatThrowsIsLoggedAndTreatedAsNoReading()
    {
        _probe.Failure = new InvalidOperationException("sensor travou");
        await StartAsync([ThermalComponent.Cpu]);

        await TickAsync(2);

        Assert.Equal(TripReason.SensorLost, Assert.Single(Trips).Reason);
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 4202 && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task AnyMemoryErrorStopsTheTest()
    {
        long errors = 0;
        _probe.Steady(ThermalComponent.Cpu, 50);
        await StartAsync([ThermalComponent.Cpu], () => Interlocked.Read(ref errors));
        Assert.Empty(Trips);

        Interlocked.Exchange(ref errors, 3);
        await TickAsync();

        var trip = Assert.Single(Trips);
        Assert.Equal(TripReason.MemoryErrors, trip.Reason);
        Assert.Equal(3, trip.Value);
    }

    [Fact]
    public async Task AWindowThatStopsRespondingTripsAfterThreeCheckupsButOneHiccupDoesNot()
    {
        var responsive = true;
        _probe.Steady(ThermalComponent.Gpu, 50);
        await StartAsync([ThermalComponent.Gpu], responsive: () => Volatile.Read(ref responsive));

        Volatile.Write(ref responsive, false);
        await TickAsync(2);
        Volatile.Write(ref responsive, true);
        await TickAsync();
        Assert.Empty(Trips);

        Volatile.Write(ref responsive, false);
        await TickAsync(3);

        Assert.Equal(TripReason.WindowUnresponsive, Assert.Single(Trips).Reason);
    }

    [Fact]
    public async Task OnlyTheFirstReasonIsKeptAndTheEventFiresOnce()
    {
        _probe.Steady(ThermalComponent.Cpu, 50);
        await StartAsync([ThermalComponent.Cpu]);

        _monitor.RequestStop();
        _monitor.RequestStop(TripReason.MemoryErrors);
        _monitor.ReportFailure(ThermalComponent.Gpu);

        var trip = Assert.Single(Trips);
        Assert.Equal(TripReason.UserRequested, trip.Reason);
    }

    [Fact]
    public async Task AFailedComponentIsReportedWithWhichOneFailed()
    {
        _probe.Steady(ThermalComponent.Gpu, 50);
        await StartAsync([ThermalComponent.Gpu]);

        _monitor.ReportFailure(ThermalComponent.Gpu);

        var trip = Assert.Single(Trips);
        Assert.Equal(TripReason.ComponentFailed, trip.Reason);
        Assert.Equal(ThermalComponent.Gpu, trip.Component);
    }

    [Fact]
    public async Task EachTickPublishesASampleWithBothComponents()
    {
        _probe.Steady(ThermalComponent.Cpu, 60).Steady(ThermalComponent.Gpu, 55);
        var samples = new List<SafetySample>();
        _monitor.SampleReady += (_, s) =>
        {
            lock (samples)
            {
                samples.Add(s);
            }
        };
        await StartAsync([ThermalComponent.Cpu, ThermalComponent.Gpu]);

        await TickAsync();

        lock (samples)
        {
            Assert.Equal(2, samples.Count);
            Assert.Equal(60, samples[1].Cpu?.Temperature);
            Assert.Equal(55, samples[1].Gpu?.Temperature);
            Assert.True(samples[1].At > samples[0].At);
        }
    }

    [Fact]
    public async Task NothingIsReadBetweenTicks()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        await StartAsync([ThermalComponent.Cpu]);
        var readsAfterFirst = _probe.Reads;

        _clock.Advance(TimeSpan.FromMilliseconds(900));
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(readsAfterFirst, _probe.Reads);
    }

    [Fact]
    public async Task StoppingEndsTheLoopAndAllowsStartingAgainFromScratch()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        await StartAsync([ThermalComponent.Cpu]);
        _monitor.RequestStop();
        await _monitor.StopAsync();
        Assert.False(_monitor.IsRunning);

        var reads = _probe.Reads;
        _clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(reads, _probe.Reads);

        await StartAsync([ThermalComponent.Cpu]);

        Assert.True(_monitor.IsRunning);
        Assert.Null(_monitor.Trip);
        Assert.Equal(60, _monitor.MaximumOf(ThermalComponent.Cpu));
    }

    [Fact]
    public async Task StartingTwiceWithoutStoppingIsRefused()
    {
        _probe.Steady(ThermalComponent.Cpu, 60);
        await StartAsync([ThermalComponent.Cpu]);

        Assert.Throws<InvalidOperationException>(() => _monitor.Start([ThermalComponent.Cpu], Limits));
    }

    [Fact]
    public void CanReadSaysWhetherAComponentHasATemperatureNow()
    {
        _probe.Steady(ThermalComponent.Cpu, 60).Steady(ThermalComponent.Gpu, null);

        Assert.True(_monitor.CanRead(ThermalComponent.Cpu));
        Assert.False(_monitor.CanRead(ThermalComponent.Gpu));
    }
}
