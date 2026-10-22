namespace M1vClock.Benchmarks.Results;

public sealed record BenchmarkMachine(
    string Cpu,
    string Gpu,
    int LogicalProcessors,
    double MemoryGigabytes,
    string OsVersion,
    string AppVersion);

public sealed record BenchmarkRecord
{
    public required Guid Id { get; init; }

    public required DateTimeOffset At { get; init; }

    public required string Test { get; init; }

    public required int ScoreVersion { get; init; }

    public required double Score { get; init; }

    public required IReadOnlyDictionary<string, double> Metrics { get; init; }

    public required BenchmarkMachine Machine { get; init; }

    public bool Sustained { get; init; }

    public bool Imported { get; init; }

    public string? Note { get; init; }
}

public sealed record BenchmarkMetricInfo(string Key, string Unit, bool HigherIsBetter, int Decimals);

public sealed record BenchmarkTestInfo(
    string Id,
    string ScoreUnit,
    int ScoreVersion,
    bool HigherIsBetter,
    int ScoreDecimals,
    IReadOnlyList<BenchmarkMetricInfo> Metrics);

public static class BenchmarkTests
{
    public const string CpuMulti = "cpu.multi";
    public const string CpuSingle = "cpu.single";
    public const string GameSimulation = "cpu.gamesim";
    public const string Memory = "memory";
    public const string Loading = "loading";
    public const string Disk = "disk";
    public const string GpuScene = "gpu.scene";
}
