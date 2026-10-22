using M1vClock.Benchmarks.Safety;
using M1vClock.Hardware.Sensors;

namespace M1vClock.App.Services;

public sealed class SensorComponentProbe : IComponentProbe
{
    private readonly ISensorService _sensors;

    public SensorComponentProbe(ISensorService sensors)
    {
        ArgumentNullException.ThrowIfNull(sensors);
        _sensors = sensors;
    }

    public ComponentSample? Read(ThermalComponent component)
    {
        var snapshot = _sensors.ReadComponent(component == ThermalComponent.Cpu ? SensorHardware.Cpu : SensorHardware.Gpu);
        if (snapshot is null)
        {
            return null;
        }

        return component == ThermalComponent.Cpu
            ? new ComponentSample(snapshot.CpuTemperature, snapshot.CpuLoad, snapshot.CpuPower, snapshot.CpuClock)
            : new ComponentSample(snapshot.GpuTemperature, snapshot.GpuLoad, snapshot.GpuPower, snapshot.GpuClock);
    }
}
