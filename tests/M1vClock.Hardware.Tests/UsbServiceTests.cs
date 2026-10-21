using System.Management;
using M1vClock.Hardware.Devices;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Hardware.Tests;

public sealed class UsbServiceTests
{
    private static WmiRow Pnp(string id, string name, string pnpClass, int problem = 0) =>
        WmiRow.Of(("DeviceID", id), ("Name", name), ("PNPClass", pnpClass), ("ConfigManagerErrorCode", problem));

    [Fact]
    public async Task ReadsOnlyTheUsbBusFromThePnpEntities()
    {
        var wmi = new FakeWmiReader().With(
            "Win32_PnPEntity",
            Pnp("USB\\VID_046D&PID_C52B\\XYZ", "Receptor Logitech", "USBDevice"),
            Pnp("PCI\\VEN_10DE&DEV_2204\\4&1&0&0008", "Placa de vídeo", "Display"),
            Pnp("USB\\ROOT_HUB30\\5&2BCE96AA&0&0", "USB Root Hub (USB 3.0)", "USB"));
        var service = new UsbDeviceService(new UsbCollector(wmi), NullLogger<UsbDeviceService>.Instance);

        var result = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.TryGet(out var devices));
        var device = Assert.Single(devices);
        Assert.Equal("Receptor Logitech", device.Name);
        Assert.Equal("Logitech", device.Maker);
        Assert.Equal("Win32_PnPEntity", Assert.Single(wmi.Queries).ClassName);
    }

    [Fact]
    public void EntriesWithoutAnIdentifierAreSkipped()
    {
        var wmi = new FakeWmiReader().With(
            "Win32_PnPEntity",
            WmiRow.Of(("Name", "Sem id"), ("PNPClass", "USB")),
            Pnp("USB\\VID_0781&PID_5581\\0123", "USB Mass Storage Device", "USB"));
        var collector = new UsbCollector(wmi);

        var devices = collector.ReadDevices();

        Assert.Equal("USB Mass Storage Device", Assert.Single(devices).Name);
    }

    [Fact]
    public async Task AWmiFailureBecomesAFailedResultAndIsLogged()
    {
        var wmi = new FakeWmiReader().Failing("Win32_PnPEntity", new ManagementException("Acesso negado."));
        var logger = new CollectingLogger<UsbDeviceService>();
        var service = new UsbDeviceService(new UsbCollector(wmi), logger);

        var result = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(logger.Entries, e => e.EventId.Id == 3001 && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ThisMachinesUsbDevicesAreReadWithoutErrors()
    {
        var service = new UsbDeviceService(new UsbCollector(new WindowsWmiReader()), NullLogger<UsbDeviceService>.Instance);

        var result = await service.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.TryGet(out var devices), result.Error);
        Assert.All(devices, d =>
        {
            Assert.StartsWith("USB\\VID_", d.InstanceId, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.Equal(4, d.Vid!.Length);
        });
    }
}
