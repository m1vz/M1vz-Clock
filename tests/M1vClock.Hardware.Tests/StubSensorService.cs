using M1vClock.Hardware.Sensors;

namespace M1vClock.Hardware.Tests;

internal sealed class StubSensorService : ISensorService
{
    public SensorServiceState State { get; set; } = SensorServiceState.Failed;

    public SensorSnapshot? Latest { get; set; }

    public int Subscriptions { get; private set; }

    public int Releases { get; private set; }

    public event EventHandler<SensorSnapshot>? SnapshotReady
    {
        add { }
        remove { }
    }

    public event EventHandler? StateChanged
    {
        add { }
        remove { }
    }

    public IDisposable Subscribe()
    {
        Subscriptions++;
        return new Lease(() => Releases++);
    }

    public IDisposable Pause() => new Lease(() => { });

    private sealed class Lease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
