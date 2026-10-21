using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware.Ram;

public interface IRamService
{
    Task<RamReport> ReadAsync(CancellationToken cancellationToken);
}

public sealed class RamService(RamCollector collector, ILogger<RamService> logger) : IRamService
{
    public async Task<RamReport> ReadAsync(CancellationToken cancellationToken)
    {
        var inventory = SectionReader.ReadAsync(logger, "módulos de memória", collector.ReadInventory, cancellationToken);
        var usage = SectionReader.ReadAsync(logger, "uso de memória", collector.ReadUsage, cancellationToken);
        await Task.WhenAll(inventory, usage).ConfigureAwait(false);
        return new RamReport(await inventory.ConfigureAwait(false), await usage.ConfigureAwait(false));
    }
}
