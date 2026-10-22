using M1vClock.Hardware.Devices;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class WmiDeviceChangeSourceTests
{
    [Fact]
    public void ThisMachineAcceptsTheDeviceChangeSubscriptionWithoutAdministrator()
    {
        using var source = new WmiDeviceChangeSource(TimeProvider.System, NullLogger<WmiDeviceChangeSource>.Instance);

        source.Start();

        Assert.True(source.IsActive);
    }

    [Fact]
    public void StartingTwiceDoesNotStackSubscriptionsAndDisposingTwiceIsHarmless()
    {
        var source = new WmiDeviceChangeSource(TimeProvider.System, NullLogger<WmiDeviceChangeSource>.Instance);

        source.Start();
        source.Start();
        source.Dispose();
        source.Dispose();

        Assert.False(source.IsActive);
    }

    [Fact]
    public async Task TheRealWatcherStartsOnThisMachineAndSeesNoChangeWhileNothingIsPlugged()
    {
        var usb = new UsbDeviceService(new UsbCollector(new M1vClock.Hardware.Wmi.WindowsWmiReader()), NullLogger<UsbDeviceService>.Instance);
        using var watcher = new DeviceWatcher(usb, new WmiDeviceChangeSource(TimeProvider.System, NullLogger<WmiDeviceChangeSource>.Instance), TimeProvider.System, NullLogger<DeviceWatcher>.Instance);
        var batches = new List<DeviceBatch>();
        watcher.DevicesChanged += (_, batch) => batches.Add(batch);

        await watcher.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.True(watcher.IsActive);
        Assert.Empty(batches);
    }
}
