namespace M1vClock.Games.Frames;

public sealed record FrameStats(
    int ProcessId,
    string ProcessName,
    double Fps,
    double FrameTimeMs,
    double OnePercentLowFps,
    IReadOnlyList<double> RecentFrameTimes);

public enum FrameMonitorState
{
    Stopped,
    Running,
    NeedsAdministrator,
    Failed,
}

public enum FrameSourceResult
{
    Started,
    NeedsAdministrator,
    Failed,
}

public readonly record struct FramePresent(int ProcessId, double TimestampMs);

public interface IFrameEventSource : IDisposable
{
    event Action<FramePresent>? Presented;

    FrameSourceResult Start();

    void Shutdown();
}

public interface IProcessNames
{
    string NameOf(int processId);
}

public interface IForegroundWindow
{
    int? ProcessId();
}

public sealed class FrameWindow
{
    public const double HistoryMs = 10_000;
    public const double FpsWindowMs = 1_000;
    public const int RecentCount = 120;
    public const int MinimumPresents = 3;

    private readonly List<double> _times = [];

    public int Count => _times.Count;

    public void Add(double timestampMs)
    {
        if (_times.Count == 0 || timestampMs > _times[^1])
        {
            _times.Add(timestampMs);
        }
        else
        {
            var index = _times.BinarySearch(timestampMs);
            if (index >= 0)
            {
                return;
            }

            _times.Insert(~index, timestampMs);
        }

        var cut = 0;
        var newest = _times[^1];
        while (cut < _times.Count && _times[cut] < newest - HistoryMs)
        {
            cut++;
        }

        if (cut > 0)
        {
            _times.RemoveRange(0, cut);
        }
    }

    public FrameStats? Compute(int processId, string processName)
    {
        if (_times.Count < MinimumPresents)
        {
            return null;
        }

        var stamps = _times.ToArray();
        var last = stamps[^1];
        var frameTimes = new double[stamps.Length - 1];
        for (var i = 1; i < stamps.Length; i++)
        {
            frameTimes[i - 1] = stamps[i] - stamps[i - 1];
        }

        var first = Array.FindIndex(stamps, t => t >= last - FpsWindowMs);
        var span = last - stamps[first];
        var frames = stamps.Length - 1 - first;
        var fps = span > 0 ? frames * 1000 / span : 0;

        var sorted = frameTimes.Order().ToArray();
        var p99 = sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(sorted.Length * 0.99))];
        var low = p99 > 0 ? 1000 / p99 : 0;

        var recent = frameTimes.Skip(Math.Max(0, frameTimes.Length - RecentCount)).ToList();
        return new FrameStats(processId, processName, fps, fps > 0 ? 1000 / fps : 0, low, recent);
    }
}
