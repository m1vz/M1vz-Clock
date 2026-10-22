using System.Globalization;
using System.IO;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Benchmarks;
using M1vClock.Benchmarks.Disk;
using M1vClock.Benchmarks.Io;
using M1vClock.Benchmarks.Memory;
using M1vClock.Benchmarks.Results;
using M1vClock.Benchmarks.Safety;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Performance;

public sealed class LatestProgress : IProgress<BenchmarkProgress>
{
    private readonly Lock _gate = new();
    private BenchmarkProgress _latest;

    public BenchmarkProgress Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public void Report(BenchmarkProgress value)
    {
        lock (_gate)
        {
            _latest = value;
        }
    }

    public BenchPoll ToPoll(string fallbackPhase)
    {
        var latest = Latest;
        return string.IsNullOrEmpty(latest.Phase) ? new BenchPoll(fallbackPhase, 0, string.Empty) : new BenchPoll(latest.Phase, latest.Fraction, string.Empty);
    }
}

public sealed class MemoryJob : IBenchJob
{
    private readonly LatestProgress _progress = new();

    public string Id => "memory";

    public string IconKey => "Icon.Memory";

    public string Title => Loc.Get("Bench.Memory.Title");

    public string Description => Loc.Get("Bench.Memory.Description");

    public IReadOnlyList<BenchOptionViewModel> Options { get; } = [];

    public IReadOnlyList<string> HistoryTests { get; } = [BenchmarkTests.Memory];

    public ThermalComponent? Thermal => null;

    public IPreviewSource? Preview => null;

    public string? UnavailableReason => null;

    public void Refresh()
    {
    }

    public BenchPoll Poll() => _progress.ToPoll("Bench.Phase.MemoryLatency");

    public async Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var benchmark = new MemoryBenchmark();
        var result = await Task.Run(() => benchmark.Run(_progress, cancellationToken), cancellationToken).ConfigureAwait(true);
        var draft = new BenchJobDraft(BenchmarkTests.Memory, 1, result.ReadGigabytesPerSecond, BenchmarkCatalog.MetricsOf(result));
        var rows = new List<InfoRowViewModel>
        {
            new(Loc.Get("Bench.Memory.Result.Latency"), Loc.Format("Bench.Result.Nanoseconds", result.LatencyNanoseconds.ToString("0.0", Loc.Culture))),
            new(Loc.Get("Bench.Memory.Result.Read"), Loc.Format("Bench.Result.GigabytesPerSecond", result.ReadGigabytesPerSecond.ToString("0.0", Loc.Culture))),
            new(Loc.Get("Bench.Memory.Result.Copy"), Loc.Format("Bench.Result.GigabytesPerSecond", result.CopyGigabytesPerSecond.ToString("0.0", Loc.Culture))),
        };
        return new BenchJobOutcome([draft], rows, [Loc.Get("Bench.Memory.Result.Note")]);
    }
}

public abstract class DriveBenchJob : IBenchJob
{
    protected DriveBenchJob(BenchmarkWorkLocation location, ILoggerFactory loggers)
    {
        Location = location;
        Loggers = loggers;
        Drive = new BenchOptionViewModel("drive", "Bench.Option.Drive", ReadDrives());
    }

    public abstract string Id { get; }

    public abstract string IconKey { get; }

    public abstract string Title { get; }

    public abstract string Description { get; }

    public IReadOnlyList<BenchOptionViewModel> Options => Extra is null ? [Drive] : [Drive, Extra];

    public abstract IReadOnlyList<string> HistoryTests { get; }

    public ThermalComponent? Thermal => null;

    public IPreviewSource? Preview => null;

    public string? UnavailableReason => Drive.Choices[0].Value.Length == 0 ? Loc.Get("Bench.Drive.None") : null;

    protected BenchmarkWorkLocation Location { get; }

    protected ILoggerFactory Loggers { get; }

    protected BenchOptionViewModel Drive { get; private set; }

    protected virtual BenchOptionViewModel? Extra => null;

    protected LatestProgress Progress { get; } = new();

    public void Refresh()
    {
        var current = Drive.Value;
        var choices = ReadDrives();
        var index = Math.Max(0, choices.ToList().FindIndex(c => c.Value == current));
        Drive = new BenchOptionViewModel("drive", "Bench.Option.Drive", choices, index);
    }

    public abstract BenchPoll Poll();

    public abstract Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken);

    protected BenchWorkPlace Place() => Location.For(Drive.Value);

    private static List<BenchChoice> ReadDrives()
    {
        var drives = DiskBenchmark.Drives();
        if (drives.Count == 0)
        {
            return [new BenchChoice(string.Empty, Loc.Get("Bench.Drive.None"))];
        }

        var system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        return [.. drives
            .OrderByDescending(d => string.Equals(d.Root, system, StringComparison.OrdinalIgnoreCase))
            .ThenBy(d => d.Root, StringComparer.OrdinalIgnoreCase)
            .Select(d => new BenchChoice(d.Root, string.IsNullOrWhiteSpace(d.Label) ? d.Root.TrimEnd('\\') : $"{d.Root.TrimEnd('\\')} {d.Label}"))];
    }
}

public sealed class LoadingJob(BenchmarkWorkLocation location, ILoggerFactory loggers) : DriveBenchJob(location, loggers)
{
    public override string Id => "loading";

    public override string IconKey => "Icon.Library";

    public override string Title => Loc.Get("Bench.Loading.Title");

    public override string Description => Loc.Get("Bench.Loading.Description");

    public override IReadOnlyList<string> HistoryTests { get; } = [BenchmarkTests.Loading];

    public override BenchPoll Poll() => Progress.ToPoll("Bench.Phase.LoadPrepare");

    public override async Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var place = Place();
        var benchmark = new LoadingBenchmark(Loggers.CreateLogger<LoadingBenchmark>());
        var result = await Task.Run(() => benchmark.Run(place.Parent, place.Hardened, Progress, cancellationToken), cancellationToken).ConfigureAwait(true);
        var draft = new BenchJobDraft(BenchmarkTests.Loading, 1, result.MapLoadSeconds, BenchmarkCatalog.MetricsOf(result));
        var rows = new List<InfoRowViewModel>
        {
            new(Loc.Get("Bench.Loading.Result.Map"), Loc.Format("Bench.Result.Seconds", result.MapLoadSeconds.ToString("0.00", Loc.Culture))),
            new(Loc.Get("Bench.Loading.Result.Sequential"), Loc.Format("Bench.Result.MegabytesPerSecond", result.SequentialReadMegabytesPerSecond.ToString("N0", Loc.Culture))),
            new(Loc.Get("Bench.Loading.Result.Small"), Loc.Format("Bench.Result.MegabytesPerSecond", result.SmallFilesMegabytesPerSecond.ToString("N0", Loc.Culture))),
            new(Loc.Get("Bench.Loading.Result.Files"), Loc.Format("Bench.Result.PerSecond", result.FilesPerSecond.ToString("N0", Loc.Culture))),
            new(Loc.Get("Bench.Result.Drive"), result.Drive),
        };
        return new BenchJobOutcome([draft], rows, [Loc.Get("Bench.Loading.Result.Note")]);
    }
}

public sealed class DiskJob : DriveBenchJob
{
    private readonly BenchOptionViewModel _size;

    public DiskJob(BenchmarkWorkLocation location, ILoggerFactory loggers)
        : base(location, loggers)
    {
        _size = new BenchOptionViewModel(
            "size",
            "Bench.Option.FileSize",
            [
                new BenchChoice("512", "512 MB"),
                new BenchChoice("1024", "1 GB"),
                new BenchChoice("4096", "4 GB"),
            ],
            1);
    }

    public override string Id => "disk";

    public override string IconKey => "Icon.Storage";

    public override string Title => Loc.Get("Bench.Disk.Title");

    public override string Description => Loc.Get("Bench.Disk.Description");

    public override IReadOnlyList<string> HistoryTests { get; } = [BenchmarkTests.Disk];

    protected override BenchOptionViewModel? Extra => _size;

    public override BenchPoll Poll() => Progress.ToPoll("Bench.Phase.DiskSeqWrite");

    public override async Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var place = Place();
        var megabytes = long.Parse(_size.Value, CultureInfo.InvariantCulture);
        var benchmark = new DiskBenchmark(Loggers.CreateLogger<DiskBenchmark>());
        var result = await benchmark.RunAsync(place.Parent, place.Hardened, megabytes, Progress, cancellationToken).ConfigureAwait(true);
        var draft = new BenchJobDraft(BenchmarkTests.Disk, DiskBenchmarkResult.ScoreVersion, result.Score, BenchmarkCatalog.MetricsOf(result));
        string Mbs(double v) => Loc.Format("Bench.Result.MegabytesPerSecond", v.ToString("N0", Loc.Culture));
        string Iops(double v) => Loc.Format("Bench.Result.Iops", v.ToString("N0", Loc.Culture));
        string Micro(double v) => Loc.Format("Bench.Result.Microseconds", v.ToString("N0", Loc.Culture));
        var rows = new List<InfoRowViewModel>
        {
            new(Loc.Get("Bench.Disk.Result.SeqReadQ8"), Mbs(result.SequentialReadQ8)),
            new(Loc.Get("Bench.Disk.Result.SeqWriteQ8"), Mbs(result.SequentialWriteQ8)),
            new(Loc.Get("Bench.Disk.Result.SeqReadQ1"), Mbs(result.SequentialReadQ1)),
            new(Loc.Get("Bench.Disk.Result.SeqWriteQ1"), Mbs(result.SequentialWriteQ1)),
            new(Loc.Get("Bench.Disk.Result.RandReadQ1"), Iops(result.RandomReadIopsQ1)),
            new(Loc.Get("Bench.Disk.Result.RandWriteQ1"), Iops(result.RandomWriteIopsQ1)),
            new(Loc.Get("Bench.Disk.Result.RandReadQ8"), Iops(result.RandomReadIopsQ8)),
            new(Loc.Get("Bench.Disk.Result.RandReadQ32"), Iops(result.RandomReadIopsQ32)),
            new(Loc.Get("Bench.Disk.Result.RandWriteQ32"), Iops(result.RandomWriteIopsQ32)),
            new(Loc.Get("Bench.Disk.Result.ReadLatency"), Micro(result.ReadLatencyMicroseconds)),
            new(Loc.Get("Bench.Disk.Result.ReadLatencyP99"), Micro(result.ReadLatencyP99Microseconds)),
            new(Loc.Get("Bench.Disk.Result.WriteLatency"), Micro(result.WriteLatencyMicroseconds)),
            new(Loc.Get("Bench.Result.Drive"), Loc.Format("Bench.Disk.Result.DriveValue", result.Drive, result.FileMegabytes)),
        };
        return new BenchJobOutcome([draft], rows, [Loc.Get("Bench.Disk.Result.Note")]);
    }
}
