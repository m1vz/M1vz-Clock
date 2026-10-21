using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Devices;

public interface IUsbDeviceService
{
    Task<ReadResult<IReadOnlyList<UsbDeviceInfo>>> ReadAsync(CancellationToken cancellationToken);
}

public sealed class UsbDeviceService(UsbCollector collector, ILogger<UsbDeviceService> logger) : IUsbDeviceService
{
    public Task<ReadResult<IReadOnlyList<UsbDeviceInfo>>> ReadAsync(CancellationToken cancellationToken) =>
        SectionReader.ReadAsync(logger, "dispositivos USB", collector.ReadDevices, cancellationToken);
}
