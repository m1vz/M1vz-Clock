using System.Diagnostics;
using M1vClock.Benchmarks.Io;
using M1vClock.Benchmarks.Statistics;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace M1vClock.Benchmarks.Disk;

public sealed record DiskBenchmarkResult(
    string Drive,
    long FileMegabytes,
    double SequentialReadQ1,
    double SequentialWriteQ1,
    double SequentialReadQ8,
    double SequentialWriteQ8,
    double RandomReadIopsQ1,
    double RandomWriteIopsQ1,
    double RandomReadIopsQ8,
    double RandomReadIopsQ32,
    double RandomWriteIopsQ32,
    double ReadLatencyMicroseconds,
    double WriteLatencyMicroseconds,
    double ReadLatencyP99Microseconds)
{
    public const int ScoreVersion = 2;

    public double Score =>
        (SequentialReadQ8 + SequentialWriteQ8) / 2
        + (RandomReadIopsQ1 + RandomWriteIopsQ1) / 200.0
        + (RandomReadIopsQ32 + RandomWriteIopsQ32) / 4000.0;
}

public sealed class DiskBenchmark
{
    public const int SequentialBlock = 1 << 20;
    public const int RandomBlock = 4096;
    public const long MinimumFileMegabytes = 16;
    public static readonly TimeSpan DefaultRandomDuration = TimeSpan.FromSeconds(4);

    private const int ProgressSteps = 9;

    private readonly ILogger _logger;
    private readonly TimeSpan _randomDuration;

    public DiskBenchmark(ILogger<DiskBenchmark> logger, TimeSpan? randomDuration = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _randomDuration = randomDuration ?? DefaultRandomDuration;
        ArgumentOutOfRangeException.ThrowIfLessThan(_randomDuration, TimeSpan.FromMilliseconds(100));
    }

    public static IReadOnlyList<DriveCandidate> Drives()
    {
        var list = new List<DriveCandidate>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            {
                continue;
            }

            if (!drive.IsReady || drive.AvailableFreeSpace < 1536L << 20)
            {
                continue;
            }

            list.Add(new DriveCandidate(drive.Name, drive.VolumeLabel, drive.AvailableFreeSpace));
        }

        return list;
    }

    public async Task<DiskBenchmarkResult> RunAsync(
        string workDirectory,
        bool hardened,
        long fileMegabytes,
        IProgress<BenchmarkProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fileMegabytes, MinimumFileMegabytes);
        var fileBytes = fileMegabytes << 20;
        DirectIo.RequireFreeSpace(workDirectory, fileBytes + DirectIo.MinimumFreeMargin);
        using var area = BenchTempArea.Create(workDirectory, hardened, _logger);
        using var handle = area.OpenNew(
            "bench.dat",
            FileAccess.ReadWrite,
            FileShare.None,
            FileOptions.Asynchronous | FileOptions.WriteThrough | DirectIo.NoBuffering,
            fileBytes);

        var step = 0;
        void Phase(string name) => progress?.Report(new BenchmarkProgress(name, step++ / (double)ProgressSteps));

        Phase("Bench.Phase.DiskSeqWrite");
        var seqWriteQ1 = await SequentialAsync(handle, fileBytes, 1, write: true, cancellationToken).ConfigureAwait(false);
        Phase("Bench.Phase.DiskSeqRead");
        var seqReadQ1 = await SequentialAsync(handle, fileBytes, 1, write: false, cancellationToken).ConfigureAwait(false);
        Phase("Bench.Phase.DiskSeqWriteQ8");
        var seqWriteQ8 = await SequentialAsync(handle, fileBytes, 8, write: true, cancellationToken).ConfigureAwait(false);
        Phase("Bench.Phase.DiskSeqReadQ8");
        var seqReadQ8 = await SequentialAsync(handle, fileBytes, 8, write: false, cancellationToken).ConfigureAwait(false);

        var slots = fileBytes / RandomBlock;
        Phase("Bench.Phase.DiskRandReadQ1");
        var readQ1 = await RandomAsync(handle, slots, 1, write: false, cancellationToken).ConfigureAwait(false);
        Phase("Bench.Phase.DiskRandWriteQ1");
        var writeQ1 = await RandomAsync(handle, slots, 1, write: true, cancellationToken).ConfigureAwait(false);
        Phase("Bench.Phase.DiskRandReadQ8");
        var readQ8 = await RandomAsync(handle, slots, 8, write: false, cancellationToken).ConfigureAwait(false);
        Phase("Bench.Phase.DiskRandReadQ32");
        var readQ32 = await RandomAsync(handle, slots, 32, write: false, cancellationToken).ConfigureAwait(false);
        Phase("Bench.Phase.DiskRandWriteQ32");
        var writeQ32 = await RandomAsync(handle, slots, 32, write: true, cancellationToken).ConfigureAwait(false);
        progress?.Report(new BenchmarkProgress("Bench.Phase.Done", 1));

        return new DiskBenchmarkResult(
            DirectIo.DriveOf(workDirectory),
            fileMegabytes,
            seqReadQ1,
            seqWriteQ1,
            seqReadQ8,
            seqWriteQ8,
            readQ1.Iops,
            writeQ1.Iops,
            readQ8.Iops,
            readQ32.Iops,
            writeQ32.Iops,
            readQ1.AverageMicroseconds,
            writeQ1.AverageMicroseconds,
            readQ1.P99Microseconds);
    }

    private static async Task<double> SequentialAsync(SafeFileHandle handle, long fileBytes, int queueDepth, bool write, CancellationToken cancellationToken)
    {
        var blocks = fileBytes / SequentialBlock;
        long next = -1;
        using var failure = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffers = new AlignedBuffer[queueDepth];
        try
        {
            for (var i = 0; i < queueDepth; i++)
            {
                buffers[i] = new AlignedBuffer(SequentialBlock);
                new Random(42 + i).NextBytes(buffers[i].GetSpan());
            }

            var clock = Stopwatch.StartNew();
            var workers = new Task[queueDepth];
            for (var w = 0; w < queueDepth; w++)
            {
                var buffer = buffers[w].Memory;
                workers[w] = Task.Run(async () =>
                {
                    try
                    {
                        long block;
                        while ((block = Interlocked.Increment(ref next)) < blocks)
                        {
                            failure.Token.ThrowIfCancellationRequested();
                            await TransferAsync(handle, buffer, block * SequentialBlock, write, failure.Token).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        await failure.CancelAsync().ConfigureAwait(false);
                        throw;
                    }
                }, CancellationToken.None);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            var seconds = clock.Elapsed.TotalSeconds;
            cancellationToken.ThrowIfCancellationRequested();
            return fileBytes / 1048576.0 / seconds;
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }

    private async Task<RandomOutcome> RandomAsync(SafeFileHandle handle, long slots, int queueDepth, bool write, CancellationToken cancellationToken)
    {
        using var failure = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffers = new AlignedBuffer[queueDepth];
        var latencies = queueDepth == 1 ? new List<double>(200_000) : null;
        try
        {
            for (var i = 0; i < queueDepth; i++)
            {
                buffers[i] = new AlignedBuffer(RandomBlock);
                new Random(42 + i).NextBytes(buffers[i].GetSpan());
            }

            var operations = new long[queueDepth];
            var clock = Stopwatch.StartNew();
            var workers = new Task[queueDepth];
            for (var w = 0; w < queueDepth; w++)
            {
                var worker = w;
                var buffer = buffers[w].Memory;
                var random = new Random((write ? 7 : 3) + 100 * w);
                workers[w] = Task.Run(async () =>
                {
                    try
                    {
                        long count = 0;
                        while (clock.Elapsed < _randomDuration)
                        {
                            failure.Token.ThrowIfCancellationRequested();
                            var offset = random.NextInt64(slots) * RandomBlock;
                            var started = latencies is null ? 0 : Stopwatch.GetTimestamp();
                            await TransferAsync(handle, buffer, offset, write, failure.Token).ConfigureAwait(false);
                            if (latencies is not null)
                            {
                                latencies.Add(Stopwatch.GetElapsedTime(started).TotalMicroseconds);
                            }

                            count++;
                        }

                        operations[worker] = count;
                    }
                    catch
                    {
                        await failure.CancelAsync().ConfigureAwait(false);
                        throw;
                    }
                }, CancellationToken.None);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            var seconds = clock.Elapsed.TotalSeconds;
            cancellationToken.ThrowIfCancellationRequested();
            var total = operations.Sum();
            var average = latencies is { Count: > 0 } ? BenchStatistics.Mean(latencies) : seconds / Math.Max(1, total) * 1e6 * queueDepth;
            var p99 = latencies is { Count: > 0 } ? BenchStatistics.Percentile(latencies, 0.99) : 0;
            return new RandomOutcome(total / seconds, average, p99);
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }

    private static async ValueTask TransferAsync(SafeFileHandle handle, Memory<byte> buffer, long offset, bool write, CancellationToken cancellationToken)
    {
        if (write)
        {
            await RandomAccess.WriteAsync(handle, buffer, offset, cancellationToken).ConfigureAwait(false);
            return;
        }

        var read = await RandomAccess.ReadAsync(handle, buffer, offset, cancellationToken).ConfigureAwait(false);
        if (read != buffer.Length)
        {
            throw new IOException("Leitura incompleta do arquivo de teste.");
        }
    }

    private readonly record struct RandomOutcome(double Iops, double AverageMicroseconds, double P99Microseconds);
}

public sealed record DriveCandidate(string Root, string Label, long FreeBytes);
