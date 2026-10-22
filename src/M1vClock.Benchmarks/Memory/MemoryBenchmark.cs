using System.Diagnostics;
using System.Runtime.InteropServices;
using M1vClock.Benchmarks.Io;

namespace M1vClock.Benchmarks.Memory;

public sealed record MemoryBenchmarkResult(double LatencyNanoseconds, double ReadGigabytesPerSecond, double CopyGigabytesPerSecond);

public sealed unsafe class MemoryBenchmark
{
    public const long DefaultLatencyBytes = 256L << 20;
    public const long DefaultBandwidthBytes = 1L << 30;
    public const int DefaultLatencySteps = 20_000_000;
    public const long SafetyMarginBytes = 512L << 20;

    private const int CacheLineLongs = 8;
    private const int WarmupSteps = 200_000;
    private const int Repetitions = 3;
    private const int PageSize = 4096;

    private readonly long _latencyBytes;
    private readonly long _bandwidthBytes;
    private readonly int _latencySteps;
    private readonly Func<long> _availableBytes;

    public MemoryBenchmark(
        long latencyBytes = DefaultLatencyBytes,
        long bandwidthBytes = DefaultBandwidthBytes,
        int latencySteps = DefaultLatencySteps,
        Func<long>? availableBytes = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(latencyBytes, 1L << 20);
        ArgumentOutOfRangeException.ThrowIfLessThan(bandwidthBytes, 1L << 20);
        ArgumentOutOfRangeException.ThrowIfLessThan(latencySteps, 1000);
        _latencyBytes = latencyBytes;
        _bandwidthBytes = bandwidthBytes;
        _latencySteps = latencySteps;
        _availableBytes = availableBytes ?? PhysicalMemory.AvailableBytes;
    }

    public long LatencyRequiredBytes => _latencyBytes + SafetyMarginBytes;

    public long BandwidthRequiredBytes => 2 * _bandwidthBytes + SafetyMarginBytes;

    public MemoryBenchmarkResult Run(IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new BenchmarkProgress("Bench.Phase.MemoryLatency", 0));
        var latency = MeasureLatencyNanoseconds(cancellationToken);
        progress?.Report(new BenchmarkProgress("Bench.Phase.MemoryBandwidth", 0.35));
        var (read, copy) = MeasureBandwidth(cancellationToken);
        progress?.Report(new BenchmarkProgress("Bench.Phase.Done", 1));
        return new MemoryBenchmarkResult(latency, read, copy);
    }

    public double MeasureLatencyNanoseconds(CancellationToken cancellationToken)
    {
        RequireMemory(LatencyRequiredBytes);
        var count = (int)(_latencyBytes / sizeof(long));
        var lines = count / CacheLineLongs;
        var order = new int[lines];
        for (var i = 0; i < lines; i++)
        {
            order[i] = i;
        }

        var random = new Random(42);
        for (var i = lines - 1; i > 0; i--)
        {
            var j = random.Next(i);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var table = Allocate((long)count * sizeof(long));
        try
        {
            var next = (long*)table;
            for (var i = 0; i < lines; i++)
            {
                next[order[i] * CacheLineLongs] = order[(i + 1) % lines] * CacheLineLongs;
            }

            long position = 0;
            for (var i = 0; i < WarmupSteps; i++)
            {
                position = next[position];
            }

            var clock = Stopwatch.StartNew();
            for (var i = 0; i < _latencySteps; i++)
            {
                position = next[position];
                if ((i & 0xFFFFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            var nanoseconds = clock.Elapsed.TotalMilliseconds * 1e6 / _latencySteps;
            GC.KeepAlive(position);
            return nanoseconds;
        }
        finally
        {
            NativeMemory.AlignedFree((void*)table);
        }
    }

    public (double Read, double Copy) MeasureBandwidth(CancellationToken cancellationToken)
    {
        RequireMemory(BandwidthRequiredBytes);
        var threads = Environment.ProcessorCount;
        var count = (int)(_bandwidthBytes / sizeof(long));
        var per = count / threads;
        var sourceAddress = Allocate((long)count * sizeof(long));
        nint destinationAddress = 0;
        try
        {
            destinationAddress = Allocate((long)count * sizeof(long));
            Parallel.For(0, threads, t => new Span<long>((long*)sourceAddress + (long)t * per, per).Fill(t + 1));

            double bestRead = 0;
            double bestCopy = 0;
            for (var repetition = 0; repetition < Repetitions; repetition++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clock = Stopwatch.StartNew();
                long total = 0;
                Parallel.For(0, threads, () => 0L, (t, _, accumulator) =>
                {
                    var span = new ReadOnlySpan<long>((long*)sourceAddress + (long)t * per, per);
                    long s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                    for (var i = 0; i + 3 < span.Length; i += 4)
                    {
                        s0 += span[i];
                        s1 += span[i + 1];
                        s2 += span[i + 2];
                        s3 += span[i + 3];
                    }

                    return accumulator + s0 + s1 + s2 + s3;
                }, accumulator => Interlocked.Add(ref total, accumulator));
                bestRead = Math.Max(bestRead, _bandwidthBytes / clock.Elapsed.TotalSeconds / 1e9);
                GC.KeepAlive(total);

                clock.Restart();
                Parallel.For(0, threads, t =>
                {
                    var from = new ReadOnlySpan<long>((long*)sourceAddress + (long)t * per, per);
                    var to = new Span<long>((long*)destinationAddress + (long)t * per, per);
                    from.CopyTo(to);
                });
                bestCopy = Math.Max(bestCopy, 2.0 * _bandwidthBytes / clock.Elapsed.TotalSeconds / 1e9);
            }

            return (bestRead, bestCopy);
        }
        finally
        {
            NativeMemory.AlignedFree((void*)sourceAddress);
            if (destinationAddress != 0)
            {
                NativeMemory.AlignedFree((void*)destinationAddress);
            }
        }
    }

    private static nint Allocate(long bytes)
    {
        var pointer = NativeMemory.AlignedAlloc((nuint)bytes, PageSize);
        NativeMemory.Clear(pointer, (nuint)bytes);
        return (nint)pointer;
    }

    private void RequireMemory(long required)
    {
        var available = _availableBytes();
        if (available < required)
        {
            throw new InsufficientMemoryException($"O teste precisa de {required >> 20} MB livres e há {available >> 20} MB.");
        }
    }
}
