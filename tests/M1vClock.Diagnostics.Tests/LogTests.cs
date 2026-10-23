using System.Text;
using M1vClock.Core.Paths;
using M1vClock.Core.Results;
using M1vClock.Diagnostics.Logs;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.Diagnostics.Tests;

public sealed class LogParserTests
{
    private static readonly string[] RealLines =
    [
        @"2026-10-22 01:04:12.998 -03:00 [INF] M1vClock.App.App: m1vClock 0.1.0 iniciado (administrador: false, dados em C:\Users\joaop\AppData\Local\m1vClock-dev).",
        "2026-10-22 01:04:13.843 -03:00 [INF] M1vClock.Hardware.Storage.StorageCollector: Sem permissão para ler MSFT_StorageReliabilityCounter; segue com o que o Windows libera sem administrador.",
        "2026-10-22 13:06:45.009 -03:00 [WRN] M1vClock.Benchmarks.Safety.SafetyMonitor: Teste interrompido pelo monitor de segurança: \"Temperature\" (Gpu) valor 63, limite 63.",
    ];

    [Fact]
    public void RealLinesOfThisPcBecomeEntriesWithTimeLevelSourceAndMessage()
    {
        var entries = LogParser.Parse(RealLines);

        Assert.Equal(3, entries.Count);
        var first = entries[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 1, 4, 12, 998, TimeSpan.FromHours(-3)), first.Time);
        Assert.Equal(LogSeverity.Information, first.Severity);
        Assert.Equal("M1vClock.App.App", first.Source);
        Assert.Equal("App", first.ShortSource);
        Assert.StartsWith("m1vClock 0.1.0 iniciado", first.Message, StringComparison.Ordinal);
        Assert.Null(first.Details);
        Assert.Equal(LogSeverity.Warning, entries[2].Severity);
        Assert.Contains("\"Temperature\" (Gpu)", entries[2].Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("VRB", LogSeverity.Verbose)]
    [InlineData("DBG", LogSeverity.Debug)]
    [InlineData("INF", LogSeverity.Information)]
    [InlineData("WRN", LogSeverity.Warning)]
    [InlineData("ERR", LogSeverity.Error)]
    [InlineData("FTL", LogSeverity.Fatal)]
    public void EveryLevelCodeOfTheLogTemplateIsUnderstood(string code, LogSeverity severity)
    {
        var entry = Assert.Single(LogParser.Parse([$"2026-10-22 01:04:12.998 -03:00 [{code}] A.B: texto"]));

        Assert.Equal(severity, entry.Severity);
    }

    [Fact]
    public void LinesThatFollowAnEntryBecomeItsDetailsAndBlankOnesAreDropped()
    {
        var entries = LogParser.Parse(
        [
            "2026-10-22 10:00:00.000 -03:00 [ERR] M1vClock.App.Pages.X: O teste não terminou.",
            "System.InvalidOperationException: quebrou",
            "   em M1vClock.App.Pages.X.Run() em X.cs:linha 10",
            string.Empty,
            "   em System.Threading.Tasks.Task.Run()",
            "2026-10-22 10:00:01.000 -03:00 [INF] M1vClock.App.App: seguiu",
        ]);

        Assert.Equal(2, entries.Count);
        Assert.Equal(LogSeverity.Error, entries[0].Severity);
        Assert.Equal("System.InvalidOperationException: quebrou\n   em M1vClock.App.Pages.X.Run() em X.cs:linha 10\n   em System.Threading.Tasks.Task.Run()", entries[0].Details);
        Assert.Null(entries[1].Details);
        Assert.Contains("quebrou", entries[0].ToPlainText(), StringComparison.Ordinal);
    }

    [Fact]
    public void LinesBeforeTheFirstEntryAreIgnoredBecauseATailCanStartInTheMiddle()
    {
        var entries = LogParser.Parse(["   em algo.cs:1", "2026-10-22 10:00:01.000 -03:00 [INF] A.B: ok"]);

        Assert.Equal("ok", Assert.Single(entries).Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("isto não é uma linha de log")]
    [InlineData("2026-10-22 10:00:01.000 [INF] A.B: sem fuso")]
    [InlineData("2026-10-22 10:00:01.000 -03:00 [XYZ] A.B: nível desconhecido")]
    [InlineData("2026-13-45 10:00:01.000 -03:00 [INF] A.B: data impossível")]
    public void LinesThatAreNotEntriesAreNeverTurnedIntoOne(string line)
    {
        Assert.Empty(LogParser.Parse([line]));
    }

    [Fact]
    public void AMessageThatItselfHasColonsKeepsEverythingAfterTheFirstSourceSeparator()
    {
        var entry = Assert.Single(LogParser.Parse(["2026-10-22 10:00:01.000 -03:00 [INF] A.B: chave: valor: outro"]));

        Assert.Equal("A.B", entry.Source);
        Assert.Equal("chave: valor: outro", entry.Message);
    }

    [Fact]
    public void NothingToParseGivesNoEntries()
    {
        Assert.Empty(LogParser.Parse([]));
    }
}

public sealed class FileLogSourceTests
{
    private static FileLogSource Create(AppPaths paths) => new(paths, NullLogger<FileLogSource>.Instance);

    private static string Line(int i) => $"2026-10-22 10:00:{i % 60:00}.000 -03:00 [INF] A.B: linha {i}";

    [Fact]
    public void OnlyLogFilesOfTheLogsFolderAreListed()
    {
        using var temp = new TempFolder();
        var logs = temp.Sub("logs");
        File.WriteAllText(Path.Combine(logs, "m1vclock-20261022.log"), Line(1));
        File.WriteAllText(Path.Combine(logs, "m1vclock-20261021.log"), Line(2));
        File.WriteAllText(Path.Combine(logs, "notas.txt"), "x");
        Directory.CreateDirectory(Path.Combine(logs, "sub.log"));
        var source = Create(new AppPaths(temp.Path, isHardened: false));

        var result = source.List();

        Assert.True(result.TryGet(out var files), result.Error);
        Assert.Equal(["m1vclock-20261021.log", "m1vclock-20261022.log"], files.Select(f => f.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ALogsFolderThatDoesNotExistYetIsEmptyNotAFailure()
    {
        using var temp = new TempFolder();

        var result = Create(new AppPaths(temp.Path, isHardened: false)).List();

        Assert.True(result.TryGet(out var files), result.Error);
        Assert.Empty(files);
    }

    [Fact]
    public void AFileThatTheLoggerStillHasOpenCanBeRead()
    {
        using var temp = new TempFolder();
        var logs = temp.Sub("logs");
        var path = Path.Combine(logs, "m1vclock-20261022.log");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writer.Write(Encoding.UTF8.GetBytes(Line(1) + "\r\n" + Line(2) + "\r\n"));
        writer.Flush();
        var source = Create(new AppPaths(temp.Path, isHardened: false));
        var file = Assert.Single(source.List().Value!);

        var result = source.Read(file, 1_000_000, CancellationToken.None);

        Assert.True(result.TryGet(out var read), result.Error);
        Assert.Equal([Line(1), Line(2)], read.Lines);
        Assert.False(read.StartsMidFile);
    }

    [Fact]
    public void ABigFileIsReadFromItsTailAndTheCutLineIsDropped()
    {
        using var temp = new TempFolder();
        var logs = temp.Sub("logs");
        var path = Path.Combine(logs, "m1vclock-20261022.log");
        File.WriteAllLines(path, Enumerable.Range(0, 400).Select(Line));
        var source = Create(new AppPaths(temp.Path, isHardened: false));
        var file = Assert.Single(source.List().Value!);

        var result = source.Read(file, 2000, CancellationToken.None);

        Assert.True(result.TryGet(out var read), result.Error);
        Assert.True(read.StartsMidFile);
        Assert.InRange(read.Lines.Count, 10, 60);
        Assert.Equal(Line(399), read.Lines[^1]);
        Assert.All(read.Lines, l => Assert.StartsWith("2026-10-22", l, StringComparison.Ordinal));
    }

    [Fact]
    public void AnAccentedMessageSurvivesTheUtf8Round()
    {
        using var temp = new TempFolder();
        var logs = temp.Sub("logs");
        var text = "2026-10-22 10:00:00.000 -03:00 [WRN] A.B: Sem permissão para ler; segue com o que o Windows libera.";
        File.WriteAllText(Path.Combine(logs, "m1vclock-20261022.log"), text + "\n", new UTF8Encoding(false));
        var source = Create(new AppPaths(temp.Path, isHardened: false));

        var read = source.Read(Assert.Single(source.List().Value!), 10_000, CancellationToken.None).Value!;

        Assert.Equal(text, Assert.Single(read.Lines));
    }

    [Fact]
    public void AFileOutsideTheLogsFolderIsRefusedEvenWithADotDotPath()
    {
        using var temp = new TempFolder();
        var logs = temp.Sub("logs");
        File.WriteAllText(Path.Combine(temp.Path, "segredo.log"), Line(1));
        var source = Create(new AppPaths(temp.Path, isHardened: false));
        var outside = new LogFile(Path.Combine(logs, "..", "segredo.log"), "segredo.log", 10, DateTimeOffset.UnixEpoch);
        var nested = new LogFile(Path.Combine(logs, "sub", "x.log"), "x.log", 10, DateTimeOffset.UnixEpoch);

        Assert.False(source.Read(outside, 1000, CancellationToken.None).IsSuccess);
        Assert.False(source.Read(nested, 1000, CancellationToken.None).IsSuccess);
    }

    [Fact]
    public void AFileThatDisappearedIsAFailureNotACrash()
    {
        using var temp = new TempFolder();
        var logs = temp.Sub("logs");
        var source = Create(new AppPaths(temp.Path, isHardened: false));
        var gone = new LogFile(Path.Combine(logs, "m1vclock-20200101.log"), "m1vclock-20200101.log", 10, DateTimeOffset.UnixEpoch);

        Assert.False(source.Read(gone, 1000, CancellationToken.None).IsSuccess);
    }

    [Fact]
    public void ACancelledReadStopsInsteadOfFinishing()
    {
        using var temp = new TempFolder();
        var logs = temp.Sub("logs");
        File.WriteAllLines(Path.Combine(logs, "m1vclock-20261022.log"), Enumerable.Range(0, 50).Select(Line));
        var source = Create(new AppPaths(temp.Path, isHardened: false));
        var file = Assert.Single(source.List().Value!);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => source.Read(file, 100_000, cancelled.Token));
    }
}

public sealed class LogServiceTests
{
    private sealed class StubSource(IReadOnlyList<LogFile> files, LogRead read) : ILogSource
    {
        public long? AskedMaximum { get; private set; }

        public ReadResult<IReadOnlyList<LogFile>> List() => ReadResult.Success(files);

        public ReadResult<LogRead> Read(LogFile file, long maximumBytes, CancellationToken cancellationToken)
        {
            AskedMaximum = maximumBytes;
            return ReadResult.Success(read);
        }
    }

    [Fact]
    public async Task TheNewestFileComesFirstAndTheNameBreaksATie()
    {
        var same = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        var source = new StubSource(
        [
            new LogFile("a", "m1vclock-20261021.log", 1, same.AddDays(-1)),
            new LogFile("b", "m1vclock-20261022.log", 1, same),
            new LogFile("c", "m1vclock-20261022_001.log", 1, same),
        ], new LogRead([], false));

        var result = await new LogService(source).ListAsync(CancellationToken.None);

        Assert.True(result.TryGet(out var files));
        Assert.Equal(["m1vclock-20261022_001.log", "m1vclock-20261022.log", "m1vclock-20261021.log"], files.Select(f => f.Name));
    }

    [Fact]
    public async Task ReadingParsesTheLinesAndAsksForAtMostFourMegabytes()
    {
        var file = new LogFile("x", "m1vclock-20261022.log", 10, DateTimeOffset.UnixEpoch);
        var source = new StubSource([file], new LogRead(["2026-10-22 10:00:01.000 -03:00 [WRN] A.B: aviso"], true));

        var result = await new LogService(source).ReadAsync(file, CancellationToken.None);

        Assert.True(result.TryGet(out var slice), result.Error);
        Assert.Equal(4L * 1024 * 1024, source.AskedMaximum);
        Assert.True(slice.StartsMidFile);
        Assert.Equal(LogSeverity.Warning, Assert.Single(slice.Entries).Severity);
    }
}

public sealed class RealMachineLogTests
{
    private static readonly AppPaths DevPaths = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.DevelopmentFolderName),
        isHardened: false);

    [Fact]
    public void EveryLineOfEveryRealLogOfThisPcIsEitherAnEntryOrADetail()
    {
        var source = new FileLogSource(DevPaths, NullLogger<FileLogSource>.Instance);
        var listed = source.List();

        Assert.True(listed.TryGet(out var files), listed.Error);
        foreach (var file in files)
        {
            var read = source.Read(file, long.MaxValue / 4, CancellationToken.None);
            Assert.True(read.TryGet(out var lines), read.Error);
            var entries = LogParser.Parse(lines.Lines);
            var headers = lines.Lines.Count(l => l.Length > 4 && char.IsAsciiDigit(l[0]) && l.Contains(" [", StringComparison.Ordinal) && l.Contains("] ", StringComparison.Ordinal));
            Assert.Equal(headers, entries.Count);
            Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Message)));
        }
    }
}
