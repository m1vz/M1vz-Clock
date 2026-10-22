using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Gpu;

public interface IGpuService
{
    Task<GpuReport> ReadAsync(CancellationToken cancellationToken);
}

public sealed class GpuService(GpuCollector collector, ILogger<GpuService> logger) : IGpuService
{
    public async Task<GpuReport> ReadAsync(CancellationToken cancellationToken)
    {
        var gpus = await SectionReader.ReadAsync(logger, "placa de vídeo", collector.Read, cancellationToken).ConfigureAwait(false);
        return new GpuReport(gpus);
    }
}
