using M1vClock.Diagnostics.Events;

namespace M1vClock.Diagnostics.Reliability;

public interface IReliabilityService
{
    Task<ReliabilityReport> ReadAsync(TimeSpan window, CancellationToken cancellationToken);
}

public sealed class ReliabilityService(IReliabilitySource source) : IReliabilityService
{
    public async Task<ReliabilityReport> ReadAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        if (window <= TimeSpan.Zero || window > EventService.MaximumWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, "A janela vai de zero a 90 dias.");
        }

        var stability = Task.Run(() => source.ReadStability(window, cancellationToken), cancellationToken);
        var records = Task.Run(() => source.ReadRecords(window, cancellationToken), cancellationToken);
        await Task.WhenAll(stability, records).ConfigureAwait(false);
        var points = stability.Result.Map<IReadOnlyList<StabilityPoint>>(p => p.OrderBy(x => x.At).ToList());
        var list = records.Result.Map<IReadOnlyList<ReliabilityRecord>>(r => r.OrderByDescending(x => x.Time).ToList());
        return new ReliabilityReport(window, points, list);
    }
}
