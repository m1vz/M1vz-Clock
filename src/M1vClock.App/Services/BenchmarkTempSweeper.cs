using System.IO;
using M1vClock.Benchmarks.Io;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Services;

public sealed class BenchmarkTempSweeper : BackgroundService
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(8);

    private readonly BenchmarkWorkLocation _location;
    private readonly ILogger<BenchmarkTempSweeper> _logger;

    public BenchmarkTempSweeper(BenchmarkWorkLocation location, ILogger<BenchmarkTempSweeper> logger)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(logger);
        _location = location;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Delay, stoppingToken).ConfigureAwait(false);
            await Task.Run(() => Sweep(stoppingToken), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.TempSweepFailed(_logger, ex);
        }
    }

    internal void Sweep(CancellationToken cancellationToken)
    {
        foreach (var parent in _location.SweepParents())
        {
            cancellationToken.ThrowIfCancellationRequested();
            BenchTempArea.SweepLeftovers(parent, _logger);
        }
    }
}
