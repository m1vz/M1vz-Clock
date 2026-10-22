using System.Runtime.InteropServices;
using M1vClock.Benchmarks.Io;
using M1vClock.Benchmarks.Safety;
using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks.Stress;

public sealed record WorkloadStatus(
    bool Alive,
    long MemoryPasses,
    long MemoryErrors,
    long MemoryBytes,
    int CpuThreads,
    long Frames,
    double Fps,
    double AverageFps,
    string? Error)
{
    public static readonly WorkloadStatus Idle = new(false, 0, 0, 0, 0, 0, 0, 0, null);
}

public interface IStressWorkload : IDisposable
{
    WorkloadStatus Status { get; }

    void Start();

    void Halt();
}

public sealed unsafe class StressRunner : IStressWorkload
{
    public const double MinimumRamFraction = 0.1;
    public const double MaximumRamFraction = 0.8;
    public const long ChunkBytes = 128L << 20;
    public const long SystemReserveBytes = 1L << 30;

    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(15);

    private readonly bool _cpu;
    private readonly bool _ram;
    private readonly double _ramFraction;
    private readonly Func<long> _availableBytes;
    private readonly ILogger _logger;
    private readonly List<Thread> _threads = [];
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation;
    private long _errors;
    private long _passes;
    private long _allocated;
    private int _cpuThreads;

    public StressRunner(bool cpu, bool ram, double ramFraction, ILogger<StressRunner> logger, Func<long>? availableBytes = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _cpu = cpu;
        _ram = ram;
        _ramFraction = Math.Clamp(ramFraction, MinimumRamFraction, MaximumRamFraction);
        _availableBytes = availableBytes ?? PhysicalMemory.AvailableBytes;
        _logger = logger;
    }

    public WorkloadStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new WorkloadStatus(
                    _cancellation is not null,
                    Interlocked.Read(ref _passes),
                    Interlocked.Read(ref _errors),
                    Interlocked.Read(ref _allocated),
                    Volatile.Read(ref _cpuThreads),
                    0,
                    0,
                    0,
                    null);
            }
        }
    }

    public static long PlannedRamBytes(long availableBytes, double fraction)
    {
        var wanted = (long)(availableBytes * Math.Clamp(fraction, MinimumRamFraction, MaximumRamFraction));
        var limited = Math.Min(wanted, availableBytes - SystemReserveBytes);
        return limited / ChunkBytes * ChunkBytes;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_cancellation is not null)
            {
                throw new InvalidOperationException("O teste de estresse já está rodando.");
            }

            _cancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _errors, 0);
            Interlocked.Exchange(ref _passes, 0);
            Interlocked.Exchange(ref _allocated, 0);
            var token = _cancellation.Token;
            var cpuThreads = Environment.ProcessorCount;
            var wantsRam = _ram && PlannedRamBytes(_availableBytes(), _ramFraction) >= ChunkBytes;
            if (_cpu && wantsRam && cpuThreads > 1)
            {
                cpuThreads--;
            }

            if (_cpu)
            {
                Volatile.Write(ref _cpuThreads, cpuThreads);
                for (var i = 0; i < cpuThreads; i++)
                {
                    var seed = i;
                    StartThread(() => CpuWorker(seed, token), $"m1vClock stress CPU {seed}");
                }
            }

            if (_ram)
            {
                var bytes = PlannedRamBytes(_availableBytes(), _ramFraction);
                if (bytes < ChunkBytes)
                {
                    SafetyLog.MemoryShort(_logger, 0, bytes >> 20);
                }
                else
                {
                    StartThread(() => RamWorker(bytes, token), "m1vClock stress RAM");
                }
            }
        }
    }

    public void Halt()
    {
        CancellationTokenSource? cancellation;
        List<Thread> threads;
        lock (_gate)
        {
            cancellation = _cancellation;
            threads = [.. _threads];
            _threads.Clear();
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        foreach (var thread in threads)
        {
            thread.Join(JoinTimeout);
        }

        lock (_gate)
        {
            _cancellation = null;
        }

        Volatile.Write(ref _cpuThreads, 0);
        cancellation.Dispose();
    }

    public void Dispose() => Halt();

    private void StartThread(ThreadStart body, string name)
    {
        var thread = new Thread(body)
        {
            IsBackground = true,
            Name = name,
            Priority = ThreadPriority.BelowNormal,
        };
        _threads.Add(thread);
        thread.Start();
    }

    private static void CpuWorker(int seed, CancellationToken token)
    {
        var buffer = new double[4096];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = i + seed + 1;
        }

        var x = (ulong)(seed + 1) * 0x9E3779B97F4A7C15UL;
        while (!token.IsCancellationRequested)
        {
            for (var repetition = 0; repetition < 64; repetition++)
            {
                for (var i = 0; i < buffer.Length; i++)
                {
                    var v = buffer[i];
                    v = Math.FusedMultiplyAdd(v, 1.0000001, 0.5);
                    v = Math.Sqrt(v * v + 1.0) + Math.Sin(v) * 1e-3;
                    buffer[i] = v > 1e12 ? i + 1 : v;
                    x ^= x << 13;
                    x ^= x >> 7;
                    x ^= x << 17;
                }
            }

            if (x == 0)
            {
                buffer[0] = 0;
            }
        }
    }

    private void RamWorker(long totalBytes, CancellationToken token)
    {
        var chunkLongs = (int)(ChunkBytes / sizeof(long));
        var chunks = new List<nint>();
        try
        {
            while ((chunks.Count + 1) * ChunkBytes <= totalBytes && !token.IsCancellationRequested)
            {
                var pointer = NativeMemory.AlignedAlloc((nuint)ChunkBytes, 4096);
                chunks.Add((nint)pointer);
                Interlocked.Add(ref _allocated, ChunkBytes);
            }
        }
        catch (OutOfMemoryException ex)
        {
            SafetyLog.MemoryShort(_logger, Interlocked.Read(ref _allocated) >> 20, totalBytes >> 20);
            SafetyLog.MemoryAllocationFailed(_logger, ex, totalBytes >> 20);
        }

        try
        {
            if (chunks.Count == 0)
            {
                return;
            }

            ulong pass = 0;
            while (!token.IsCancellationRequested)
            {
                pass++;
                var pattern = unchecked((long)(pass * 0x9E3779B97F4A7C15UL));
                foreach (var chunk in chunks)
                {
                    var span = new Span<long>((void*)chunk, chunkLongs);
                    for (var i = 0; i < span.Length; i++)
                    {
                        span[i] = pattern ^ i;
                    }

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                }

                foreach (var chunk in chunks)
                {
                    var span = new ReadOnlySpan<long>((void*)chunk, chunkLongs);
                    long errors = 0;
                    for (var i = 0; i < span.Length; i++)
                    {
                        if (span[i] != (pattern ^ i))
                        {
                            errors++;
                        }
                    }

                    if (errors > 0)
                    {
                        Interlocked.Add(ref _errors, errors);
                    }

                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                }

                Interlocked.Increment(ref _passes);
            }
        }
        finally
        {
            foreach (var chunk in chunks)
            {
                NativeMemory.AlignedFree((void*)chunk);
            }

            Interlocked.Exchange(ref _allocated, 0);
        }
    }
}
