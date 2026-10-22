using System.Diagnostics;
using M1vClock.Benchmarks.Io;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace M1vClock.Benchmarks.Memory;

public sealed record LoadingBenchmarkResult(
    double SequentialReadMegabytesPerSecond,
    double SmallFilesMegabytesPerSecond,
    double FilesPerSecond,
    double MapLoadSeconds,
    string Drive);

public sealed class LoadingBenchmark
{
    public const long DefaultPackBytes = 512L << 20;
    public const int DefaultAssetCount = 1000;
    public const int AssetBytes = 64 << 10;
    public const int ReaderThreads = 8;

    private const int Chunk = 4 << 20;

    private readonly long _packBytes;
    private readonly int _assetCount;
    private readonly ILogger _logger;

    public LoadingBenchmark(ILogger<LoadingBenchmark> logger, long packBytes = DefaultPackBytes, int assetCount = DefaultAssetCount)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(packBytes, Chunk);
        ArgumentOutOfRangeException.ThrowIfNotEqual(packBytes % Chunk, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(assetCount, ReaderThreads);
        _logger = logger;
        _packBytes = packBytes;
        _assetCount = assetCount;
    }

    public long RequiredBytes => _packBytes + (long)_assetCount * AssetBytes + DirectIo.MinimumFreeMargin;

    public LoadingBenchmarkResult Run(string workDirectory, bool hardened, IProgress<BenchmarkProgress>? progress, CancellationToken cancellationToken)
    {
        DirectIo.RequireFreeSpace(workDirectory, RequiredBytes);
        using var area = BenchTempArea.Create(workDirectory, hardened, _logger);
        var keepers = new List<SafeFileHandle>(_assetCount);
        try
        {
            using var block = new AlignedBuffer(Chunk);
            new Random(7).NextBytes(block.GetSpan());
            var total = _packBytes + (long)_assetCount * AssetBytes;

            using var pack = area.OpenNew("map.pack", FileAccess.ReadWrite, FileShare.None, FileOptions.WriteThrough | DirectIo.NoBuffering, _packBytes);
            for (long offset = 0; offset < _packBytes; offset += Chunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RandomAccess.Write(pack, block.GetSpan(), offset);
                progress?.Report(new BenchmarkProgress("Bench.Phase.LoadPrepare", offset / (double)total));
            }

            for (var i = 0; i < _assetCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var keeper = area.OpenNew(AssetName(i), FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, FileOptions.None);
                keepers.Add(keeper);
                RandomAccess.Write(keeper, block.GetSpan()[..AssetBytes], 0);
                RandomAccess.FlushToDisk(keeper);
                if (i % 50 == 0)
                {
                    progress?.Report(new BenchmarkProgress("Bench.Phase.LoadPrepare", (_packBytes + (double)i * AssetBytes) / total));
                }
            }

            var clock = Stopwatch.StartNew();
            for (long offset = 0; offset < _packBytes; offset += Chunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = RandomAccess.Read(pack, block.GetSpan(), offset);
                if (read != Chunk)
                {
                    throw new IOException("Leitura incompleta do pacote de teste.");
                }

                progress?.Report(new BenchmarkProgress("Bench.Phase.LoadPack", offset / (double)_packBytes));
            }

            var sequential = _packBytes / 1048576.0 / clock.Elapsed.TotalSeconds;

            progress?.Report(new BenchmarkProgress("Bench.Phase.LoadAntivirus", 0));
            ReadAssets(area, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new BenchmarkProgress("Bench.Phase.LoadAssets", 0.5));
            clock.Restart();
            ReadAssets(area, cancellationToken);
            var seconds = clock.Elapsed.TotalSeconds;
            cancellationToken.ThrowIfCancellationRequested();

            var mapSeconds = _packBytes / 1048576.0 / sequential + seconds;
            var smallRate = (double)_assetCount * AssetBytes / 1048576.0 / seconds;
            progress?.Report(new BenchmarkProgress("Bench.Phase.Done", 1));
            return new LoadingBenchmarkResult(sequential, smallRate, _assetCount / seconds, mapSeconds, DirectIo.DriveOf(workDirectory));
        }
        finally
        {
            foreach (var keeper in keepers)
            {
                keeper.Dispose();
            }
        }
    }

    private static string AssetName(int index) => $"asset{index:D4}.dat";

    private void ReadAssets(BenchTempArea area, CancellationToken cancellationToken)
    {
        Parallel.For(0, ReaderThreads, new ParallelOptions { MaxDegreeOfParallelism = ReaderThreads }, thread =>
        {
            using var buffer = new AlignedBuffer(AssetBytes);
            for (var i = thread; i < _assetCount && !cancellationToken.IsCancellationRequested; i += ReaderThreads)
            {
                using var handle = File.OpenHandle(
                    area.PathOf(AssetName(i)),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    DirectIo.NoBuffering);
                RandomAccess.Read(handle, buffer.GetSpan(), 0);
            }
        });
    }
}
