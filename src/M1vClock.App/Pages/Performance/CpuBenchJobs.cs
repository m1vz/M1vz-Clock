using System.Globalization;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Hardware;
using M1vClock.Benchmarks.Cpu;
using M1vClock.Benchmarks.Results;
using M1vClock.Benchmarks.Safety;

namespace M1vClock.App.Pages.Performance;

public sealed class PathTracePreview(PathTracer tracer, int factor = PathTracePreview.DefaultFactor) : IPreviewSource
{
    public const int DefaultFactor = 2;

    public int PixelWidth => tracer.Width / factor;

    public int PixelHeight => tracer.Height / factor;

    public void CopyPixels(Span<int> destination) => tracer.CopyDownsampledPixelsTo(destination, factor);

    public IReadOnlyList<PreviewRegion> ActiveRegions()
    {
        var regions = new List<PreviewRegion>();
        foreach (var index in tracer.ActiveBuckets())
        {
            var bucket = tracer.Buckets[index];
            regions.Add(new PreviewRegion(bucket.X / factor, bucket.Y / factor, Math.Max(1, bucket.Width / factor), Math.Max(1, bucket.Height / factor)));
        }

        return regions;
    }
}

public sealed class BattlePreview : IPreviewSource
{
    public const int Size = 440;

    private const int Background = unchecked((int)0xFF101010);
    private const int TeamA = unchecked((int)0xFFF2F2F2);
    private const int TeamB = unchecked((int)0xFF6B6B6B);

    private readonly GameSimulation _simulation;
    private readonly float[] _x;
    private readonly float[] _y;
    private readonly byte[] _team;

    public BattlePreview(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        _simulation = simulation;
        _x = new float[simulation.Entities];
        _y = new float[simulation.Entities];
        _team = new byte[simulation.Entities];
    }

    public int PixelWidth => Size;

    public int PixelHeight => Size;

    public void CopyPixels(Span<int> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, Size * Size);
        destination[..(Size * Size)].Fill(Background);
        _simulation.CopyView(_x, _y, _team);
        const float scale = Size / GameSimulation.World;
        for (var i = 0; i < _x.Length; i++)
        {
            var px = (int)(_x[i] * scale);
            var py = (int)(_y[i] * scale);
            if ((uint)px >= Size || (uint)py >= Size)
            {
                continue;
            }

            destination[py * Size + px] = _team[i] == 0 ? TeamA : TeamB;
        }
    }

    public IReadOnlyList<PreviewRegion> ActiveRegions() => [];
}

public sealed class CpuRenderJob : IBenchJob
{
    private readonly BenchOptionViewModel _mode;
    private readonly BenchOptionViewModel _duration;
    private volatile PathTracer? _tracer;
    private volatile string _phase = "Bench.Phase.CpuMulti";
    private long _startedAt;
    private TimeSpan _minimum;

    public CpuRenderJob()
    {
        _mode = new BenchOptionViewModel(
            "mode",
            "Bench.Option.Mode",
            [
                new BenchChoice("both", Loc.Get("Bench.Cpu.Mode.Both")),
                new BenchChoice("multi", Loc.Get("Bench.Cpu.Mode.Multi")),
                new BenchChoice("single", Loc.Get("Bench.Cpu.Mode.Single")),
            ]);
        _duration = new BenchOptionViewModel(
            "duration",
            "Bench.Option.Duration",
            [
                new BenchChoice("normal", Loc.Get("Bench.Cpu.Duration.Normal")),
                new BenchChoice("sustained", Loc.Get("Bench.Cpu.Duration.Sustained")),
            ]);
        Options = [_mode, _duration];
    }

    public string Id => "cpu";

    public string IconKey => "Icon.Cpu";

    public string Title => Loc.Get("Bench.Cpu.Title");

    public string Description => Loc.Get("Bench.Cpu.Description");

    public IReadOnlyList<BenchOptionViewModel> Options { get; }

    public IReadOnlyList<string> HistoryTests { get; } = [BenchmarkTests.CpuMulti, BenchmarkTests.CpuSingle];

    public ThermalComponent? Thermal => ThermalComponent.Cpu;

    public IPreviewSource? Preview { get; private set; }

    public string? UnavailableReason => null;

    public void Refresh()
    {
    }

    public BenchPoll Poll()
    {
        var tracer = _tracer;
        if (tracer is null)
        {
            return BenchPoll.Idle;
        }

        var pass = tracer.BucketsTotal == 0 ? 0 : (double)tracer.BucketsDone / tracer.BucketsTotal;
        var detail = string.Empty;
        if (_minimum > TimeSpan.Zero && _startedAt != 0)
        {
            var elapsed = TimeProvider.System.GetElapsedTime(_startedAt);
            detail = Loc.Format("Bench.Cpu.Sustained.Detail", FormatSpan(elapsed), FormatSpan(_minimum));
            pass = Math.Min(1, elapsed / _minimum);
        }

        return new BenchPoll(_phase, pass, detail);
    }

    public async Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var sustained = _duration.Value == "sustained";
        _minimum = sustained ? TimeSpan.FromMinutes(10) : TimeSpan.Zero;
        var tracer = new PathTracer();
        _tracer = tracer;
        Preview = new PathTracePreview(tracer);
        var mode = _mode.Value;
        var threads = CpuTopology.LogicalProcessors;
        var drafts = new List<BenchJobDraft>();
        var rows = new List<InfoRowViewModel>();
        double? multi = null;
        double? single = null;
        try
        {
            if (mode is "both" or "multi")
            {
                _phase = "Bench.Phase.CpuMulti";
                _startedAt = TimeProvider.System.GetTimestamp();
                var result = await Task.Run(() => tracer.Run(threads, _minimum, cancellationToken), cancellationToken).ConfigureAwait(true);
                multi = result.Score;
                drafts.Add(new BenchJobDraft(BenchmarkTests.CpuMulti, 1, result.Score, BenchmarkCatalog.MetricsOf(result), sustained));
                rows.Add(new InfoRowViewModel(Loc.Get("Bench.Cpu.Result.Multi"), Loc.Format("Bench.Result.Points", result.Score.ToString("N0", Loc.Culture))));
                rows.Add(new InfoRowViewModel(Loc.Get("Bench.Cpu.Result.MultiTime"), Loc.Format("Bench.Cpu.Result.TimeThreads", result.Seconds.ToString("0.0", Loc.Culture), result.Threads)));
            }

            if (mode is "both" or "single")
            {
                _phase = "Bench.Phase.CpuSingle";
                _startedAt = TimeProvider.System.GetTimestamp();
                var result = await Task.Run(() => tracer.Run(1, _minimum, cancellationToken), cancellationToken).ConfigureAwait(true);
                single = result.Score;
                drafts.Add(new BenchJobDraft(BenchmarkTests.CpuSingle, 1, result.Score, BenchmarkCatalog.MetricsOf(result), sustained));
                rows.Add(new InfoRowViewModel(Loc.Get("Bench.Cpu.Result.Single"), Loc.Format("Bench.Result.Points", result.Score.ToString("N0", Loc.Culture))));
                rows.Add(new InfoRowViewModel(Loc.Get("Bench.Cpu.Result.SingleTime"), Loc.Format("Bench.Cpu.Result.Time", result.Seconds.ToString("0.0", Loc.Culture))));
            }
        }
        finally
        {
            _startedAt = 0;
        }

        var notes = new List<string>();
        if (multi is { } m && single is { } s && s > 0)
        {
            rows.Add(new InfoRowViewModel(Loc.Get("Bench.Cpu.Result.Ratio"), Loc.Format("Bench.Cpu.Result.RatioValue", (m / s).ToString("0.0", Loc.Culture), threads)));
            notes.Add(Loc.Format("Bench.Cpu.Result.RatioNote", CpuTopology.PhysicalCores()));
        }

        return new BenchJobOutcome(drafts, rows, notes);
    }

    internal static string FormatSpan(TimeSpan span) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}:{span.Seconds:00}");
}

public sealed class GameSimJob : IBenchJob
{
    public const int Passes = 3;

    private readonly BenchOptionViewModel _length;
    private volatile GameSimulation? _simulation;
    private int _pass;

    public GameSimJob()
    {
        _length = new BenchOptionViewModel(
            "length",
            "Bench.Option.Duration",
            [
                new BenchChoice("8", Loc.Get("Bench.Sim.Length.Quick")),
                new BenchChoice("20", Loc.Get("Bench.Sim.Length.Full")),
            ]);
        Options = [_length];
    }

    public string Id => "sim";

    public string IconKey => "Icon.Fps";

    public string Title => Loc.Get("Bench.Sim.Title");

    public string Description => Loc.Get("Bench.Sim.Description");

    public IReadOnlyList<BenchOptionViewModel> Options { get; }

    public IReadOnlyList<string> HistoryTests { get; } = [BenchmarkTests.GameSimulation];

    public ThermalComponent? Thermal => ThermalComponent.Cpu;

    public IPreviewSource? Preview { get; private set; }

    public string? UnavailableReason => null;

    public void Refresh()
    {
    }

    public BenchPoll Poll()
    {
        var simulation = _simulation;
        if (simulation is null)
        {
            return BenchPoll.Idle;
        }

        var pass = Volatile.Read(ref _pass);
        if (simulation.IsWarmingUp)
        {
            return new BenchPoll("Bench.Phase.SimWarmup", Math.Max(0, pass - 1) / (double)Passes, Loc.Format("Bench.Sim.Pass", pass, Passes));
        }

        var detail = simulation.CurrentFps > 0
            ? Loc.Format("Bench.Sim.Live", pass, Passes, simulation.CurrentFps.ToString("0", Loc.Culture))
            : Loc.Format("Bench.Sim.Pass", pass, Passes);
        return new BenchPoll("Bench.Phase.Sim", (Math.Max(1, pass) - 1 + simulation.Progress) / Passes, detail);
    }

    public async Task<BenchJobOutcome> RunAsync(CancellationToken cancellationToken)
    {
        var simulation = new GameSimulation();
        _simulation = simulation;
        Preview = new BattlePreview(simulation);
        var seconds = int.Parse(_length.Value, CultureInfo.InvariantCulture);
        Volatile.Write(ref _pass, 1);
        var summary = await Task.Run(
            () => simulation.RunPasses(Passes, TimeSpan.FromSeconds(seconds), GameSimulation.DefaultWarmup, pass => Volatile.Write(ref _pass, pass), cancellationToken),
            cancellationToken).ConfigureAwait(true);

        var draft = new BenchJobDraft(BenchmarkTests.GameSimulation, 1, summary.Score, BenchmarkCatalog.MetricsOf(summary, simulation.Entities));
        var rows = new List<InfoRowViewModel>
        {
            new(Loc.Get("Bench.Sim.Result.Fps"), Loc.Format("Bench.Result.Fps", summary.AverageFps.ToString("0", Loc.Culture))),
            new(Loc.Get("Bench.Sim.Result.Low"), Loc.Format("Bench.Result.Fps", summary.LowFps.ToString("0", Loc.Culture))),
            new(Loc.Get("Bench.Sim.Result.Main"), Loc.Format("Bench.Result.PerFrame", summary.MainThreadMilliseconds.ToString("0.00", Loc.Culture))),
            new(Loc.Get("Bench.Sim.Result.Parallel"), Loc.Format("Bench.Result.PerFrame", summary.ParallelMilliseconds.ToString("0.00", Loc.Culture))),
            new(Loc.Get("Bench.Sim.Result.Units"), simulation.Entities.ToString("N0", Loc.Culture)),
            new(Loc.Get("Bench.Sim.Result.Workers"), summary.Workers.ToString(Loc.Culture)),
            new(Loc.Get("Bench.Sim.Result.Passes"), string.Join(" · ", summary.Passes.Select(p => p.AverageFps.ToString("0", Loc.Culture)))),
            new(Loc.Get("Bench.Sim.Result.Spread"), Loc.Format("Bench.Result.Percent", summary.SpreadPercent.ToString("0.0", Loc.Culture))),
        };
        return new BenchJobOutcome([draft], rows, [Loc.Get("Bench.Sim.Result.Note")]);
    }
}
