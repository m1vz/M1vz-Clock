using M1vClock.Benchmarks.Cpu;
using M1vClock.Benchmarks.Disk;
using M1vClock.Benchmarks.Memory;

namespace M1vClock.Benchmarks.Results;

public static class BenchmarkCatalog
{
    private static readonly BenchmarkTestInfo[] Tests =
    [
        new(
            BenchmarkTests.CpuMulti,
            "pts",
            1,
            true,
            0,
            [
                new("seconds", "s", false, 1),
                new("passes", string.Empty, true, 0),
                new("threads", string.Empty, true, 0),
            ]),
        new(
            BenchmarkTests.CpuSingle,
            "pts",
            1,
            true,
            0,
            [
                new("seconds", "s", false, 1),
                new("passes", string.Empty, true, 0),
                new("threads", string.Empty, true, 0),
            ]),
        new(
            BenchmarkTests.GameSimulation,
            "fps",
            1,
            true,
            1,
            [
                new("average_fps", "fps", true, 1),
                new("low_fps", "fps", true, 1),
                new("main_ms", "ms", false, 2),
                new("parallel_ms", "ms", false, 2),
                new("frames", string.Empty, true, 0),
                new("entities", string.Empty, true, 0),
                new("workers", string.Empty, true, 0),
                new("spread_percent", "%", false, 1),
            ]),
        new(
            BenchmarkTests.Memory,
            "GB/s",
            1,
            true,
            1,
            [
                new("read_gbs", "GB/s", true, 1),
                new("copy_gbs", "GB/s", true, 1),
                new("latency_ns", "ns", false, 1),
            ]),
        new(
            BenchmarkTests.Loading,
            "s",
            1,
            false,
            2,
            [
                new("map_seconds", "s", false, 2),
                new("sequential_mbs", "MB/s", true, 0),
                new("small_files_mbs", "MB/s", true, 0),
                new("files_per_second", "/s", true, 0),
            ]),
        new(
            BenchmarkTests.Disk,
            "pts",
            DiskBenchmarkResult.ScoreVersion,
            true,
            0,
            [
                new("seq_read_q1", "MB/s", true, 0),
                new("seq_write_q1", "MB/s", true, 0),
                new("seq_read_q8", "MB/s", true, 0),
                new("seq_write_q8", "MB/s", true, 0),
                new("rand_read_q1", "IOPS", true, 0),
                new("rand_write_q1", "IOPS", true, 0),
                new("rand_read_q8", "IOPS", true, 0),
                new("rand_read_q32", "IOPS", true, 0),
                new("rand_write_q32", "IOPS", true, 0),
                new("read_latency_us", "µs", false, 0),
                new("write_latency_us", "µs", false, 0),
                new("read_latency_p99_us", "µs", false, 0),
                new("file_mb", "MB", true, 0),
            ]),
        new(
            BenchmarkTests.GpuScene,
            "pts",
            1,
            true,
            0,
            [
                new("average_fps", "fps", true, 1),
                new("low_fps", "fps", true, 1),
                new("frames", string.Empty, true, 0),
                new("seconds", "s", false, 1),
                new("width", "px", true, 0),
                new("height", "px", true, 0),
            ]),
    ];

    public static IReadOnlyList<BenchmarkTestInfo> All => Tests;

    public static BenchmarkTestInfo? Find(string test)
    {
        foreach (var info in Tests)
        {
            if (string.Equals(info.Id, test, StringComparison.Ordinal))
            {
                return info;
            }
        }

        return null;
    }

    public static BenchmarkMetricInfo? FindMetric(string test, string key)
    {
        var info = Find(test);
        if (info is null)
        {
            return null;
        }

        foreach (var metric in info.Metrics)
        {
            if (string.Equals(metric.Key, key, StringComparison.Ordinal))
            {
                return metric;
            }
        }

        return null;
    }

    public static Dictionary<string, double> MetricsOf(PathTraceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new Dictionary<string, double>
        {
            ["seconds"] = result.Seconds,
            ["passes"] = result.Passes,
            ["threads"] = result.Threads,
        };
    }

    public static Dictionary<string, double> MetricsOf(GameSimulationSummary result, int entities)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new Dictionary<string, double>
        {
            ["average_fps"] = result.AverageFps,
            ["low_fps"] = result.LowFps,
            ["main_ms"] = result.MainThreadMilliseconds,
            ["parallel_ms"] = result.ParallelMilliseconds,
            ["frames"] = result.Frames,
            ["entities"] = entities,
            ["workers"] = result.Workers,
            ["spread_percent"] = result.SpreadPercent,
        };
    }

    public static Dictionary<string, double> MetricsOf(MemoryBenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new Dictionary<string, double>
        {
            ["read_gbs"] = result.ReadGigabytesPerSecond,
            ["copy_gbs"] = result.CopyGigabytesPerSecond,
            ["latency_ns"] = result.LatencyNanoseconds,
        };
    }

    public static Dictionary<string, double> MetricsOf(LoadingBenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new Dictionary<string, double>
        {
            ["map_seconds"] = result.MapLoadSeconds,
            ["sequential_mbs"] = result.SequentialReadMegabytesPerSecond,
            ["small_files_mbs"] = result.SmallFilesMegabytesPerSecond,
            ["files_per_second"] = result.FilesPerSecond,
        };
    }

    public static Dictionary<string, double> MetricsOf(DiskBenchmarkResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new Dictionary<string, double>
        {
            ["seq_read_q1"] = result.SequentialReadQ1,
            ["seq_write_q1"] = result.SequentialWriteQ1,
            ["seq_read_q8"] = result.SequentialReadQ8,
            ["seq_write_q8"] = result.SequentialWriteQ8,
            ["rand_read_q1"] = result.RandomReadIopsQ1,
            ["rand_write_q1"] = result.RandomWriteIopsQ1,
            ["rand_read_q8"] = result.RandomReadIopsQ8,
            ["rand_read_q32"] = result.RandomReadIopsQ32,
            ["rand_write_q32"] = result.RandomWriteIopsQ32,
            ["read_latency_us"] = result.ReadLatencyMicroseconds,
            ["write_latency_us"] = result.WriteLatencyMicroseconds,
            ["read_latency_p99_us"] = result.ReadLatencyP99Microseconds,
            ["file_mb"] = result.FileMegabytes,
        };
    }
}
