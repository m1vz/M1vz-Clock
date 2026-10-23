using System.ComponentModel;
using System.Globalization;
using System.Text;
using M1vClock.Core.Processes;
using M1vClock.Core.Security;
using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Repair;

public sealed class WindowsSfcSource : ISfcSource
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(45);

    private readonly ElevationInfo _elevation;
    private readonly IProcessRunner _runner;
    private readonly ILogger<WindowsSfcSource> _logger;
    private readonly string _logPath;

    public WindowsSfcSource(ElevationInfo elevation, IProcessRunner runner, ILogger<WindowsSfcSource> logger)
        : this(elevation, runner, logger, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs", "CBS", "CBS.log"))
    {
    }

    public WindowsSfcSource(ElevationInfo elevation, IProcessRunner runner, ILogger<WindowsSfcSource> logger, string logPath)
    {
        ArgumentNullException.ThrowIfNull(elevation);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        _elevation = elevation;
        _runner = runner;
        _logger = logger;
        _logPath = logPath;
    }

    public async Task<SfcOutcome> VerifyAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!_elevation.IsElevated)
        {
            return new SfcOutcome(SfcStatus.NeedsAdministrator, -1, false, false, null);
        }

        var logStart = LengthOf();
        ProcessResult result;
        try
        {
            var request = new ProcessRequest(SystemTools.System32("sfc.exe"), ["/verifyonly"])
            {
                Timeout = Limit,
                OutputEncoding = Encoding.Unicode,
            };
            result = await _runner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new SfcOutcome(SfcStatus.Cancelled, -1, false, false, null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            RepairLog.SfcStartFailed(_logger, ex);
            return new SfcOutcome(SfcStatus.Failed, -1, false, false, null);
        }

        if (result.TimedOut)
        {
            return new SfcOutcome(SfcStatus.Failed, result.ExitCode, false, false, result.ExitCode.ToString(CultureInfo.InvariantCulture));
        }

        return SfcOutput.Analyze(result.ExitCode, LinesSince(logStart));
    }

    private long LengthOf()
    {
        try
        {
            return new FileInfo(_logPath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RepairLog.CbsReadFailed(_logger, ex);
            return 0;
        }
    }

    private List<string> LinesSince(long start)
    {
        var lines = new List<string>();
        try
        {
            using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(start <= stream.Length ? start : 0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (SfcOutput.IsRelevant(line))
                {
                    lines.Add(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RepairLog.CbsReadFailed(_logger, ex);
        }

        return lines;
    }
}
