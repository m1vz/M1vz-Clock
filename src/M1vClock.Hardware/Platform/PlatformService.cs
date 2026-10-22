using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Platform;

public interface IPlatformService
{
    Task<PlatformReport> ReadAsync(CancellationToken cancellationToken);
}

public sealed class PlatformService(PlatformCollector collector, ILogger<PlatformService> logger) : IPlatformService
{
    public async Task<PlatformReport> ReadAsync(CancellationToken cancellationToken)
    {
        var os = SectionReader.ReadAsync(logger, "sistema operacional", collector.ReadOs, cancellationToken);
        var board = SectionReader.ReadAsync(logger, "placa-mãe", collector.ReadBoard, cancellationToken);
        var bios = SectionReader.ReadAsync(logger, "BIOS", collector.ReadBios, cancellationToken);
        var cpus = SectionReader.ReadAsync(logger, "processador", collector.ReadCpus, cancellationToken);
        await Task.WhenAll(os, board, bios, cpus).ConfigureAwait(false);
        return new PlatformReport(
            await os.ConfigureAwait(false),
            await board.ConfigureAwait(false),
            await bios.ConfigureAwait(false),
            (await cpus.ConfigureAwait(false)).Map(list => (IReadOnlyList<CpuInfo>)list));
    }
}
