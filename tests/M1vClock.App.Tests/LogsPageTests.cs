using System.Globalization;
using System.Resources;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Diagnostics;
using M1vClock.App.Services;
using M1vClock.Core.Paths;
using M1vClock.Core.Results;
using M1vClock.Core.Shell;
using M1vClock.Diagnostics.Logs;
using Microsoft.Extensions.Time.Testing;
using DiagLogEntry = M1vClock.Diagnostics.Logs.LogEntry;

namespace M1vClock.App.Tests;

public sealed class LogRowsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    private static DiagLogEntry Entry(int minute, LogSeverity severity, string message, string source = "M1vClock.App.App", string? details = null) =>
        new(Now.AddMinutes(minute), severity, source, message, details);

    [Fact]
    public void TheFileLabelSaysTodayYesterdayOrTheDateAndAlwaysTheSize()
    {
        var today = new LogFile("a", "a.log", 126_851, Now);
        var yesterday = new LogFile("b", "b.log", 152_939, Now.AddDays(-1));
        var older = new LogFile("c", "c.log", 10_290, Now.AddDays(-5));

        Assert.Equal("Hoje · 123,88 KB", LogRows.FileLabel(today, Now));
        Assert.Equal("Ontem · 149,35 KB", LogRows.FileLabel(yesterday, Now));
        Assert.Equal("17/10 · 10,05 KB", LogRows.FileLabel(older, Now));
    }

    [Fact]
    public void TheEntriesComeNewestFirstAndTheLevelFilterKeepsOnlyWhatIsAtLeastThatSerious()
    {
        var entries = new[]
        {
            Entry(0, LogSeverity.Information, "a"),
            Entry(1, LogSeverity.Warning, "b"),
            Entry(2, LogSeverity.Error, "c"),
            Entry(3, LogSeverity.Information, "d"),
        };

        Assert.Equal(["d", "c", "b", "a"], LogRows.Filter(entries, 0, string.Empty, out var all).Select(r => r.Message));
        Assert.Equal(["c", "b"], LogRows.Filter(entries, 1, string.Empty, out var problems).Select(r => r.Message));
        Assert.Equal(["c"], LogRows.Filter(entries, 2, string.Empty, out var errors).Select(r => r.Message));
        Assert.Equal((4, 2, 1), (all, problems, errors));
    }

    [Fact]
    public void TheSearchLooksInTheMessageTheSourceAndTheDetailsIgnoringCase()
    {
        var entries = new[]
        {
            Entry(0, LogSeverity.Information, "Sensores abertos"),
            Entry(1, LogSeverity.Warning, "outra coisa", source: "M1vClock.Hardware.Storage.StorageCollector"),
            Entry(2, LogSeverity.Error, "falhou", details: "System.InvalidOperationException: quebrou"),
        };

        Assert.Equal(["Sensores abertos"], LogRows.Filter(entries, 0, "sensores", out _).Select(r => r.Message));
        Assert.Equal(["outra coisa"], LogRows.Filter(entries, 0, "STORAGE", out _).Select(r => r.Message));
        Assert.Equal(["falhou"], LogRows.Filter(entries, 0, "invalidoperation", out _).Select(r => r.Message));
        Assert.Empty(LogRows.Filter(entries, 0, "não existe", out var none));
        Assert.Equal(0, none);
        Assert.Equal(3, LogRows.Filter(entries, 0, "   ", out _).Count);
    }

    [Fact]
    public void OnlyTheNewestThreeHundredAreShownButTheMatchCountIsTheRealOne()
    {
        var entries = Enumerable.Range(0, 450).Select(i => Entry(i, LogSeverity.Information, $"m{i}")).ToList();

        var shown = LogRows.Filter(entries, 0, string.Empty, out var matching);

        Assert.Equal(LogRows.MaximumShown, shown.Count);
        Assert.Equal(450, matching);
        Assert.Equal("m449", shown[0].Message);
        Assert.Equal("m150", shown[^1].Message);
    }

    [Fact]
    public void AnEntryRowCarriesTheTimeTheSeverityTextAndToneAndTheShortSource()
    {
        var row = new LogEntryViewModel(Entry(0, LogSeverity.Warning, "Teste interrompido", source: "M1vClock.Benchmarks.Safety.SafetyMonitor"));

        Assert.Equal(Now.ToLocalTime().ToString("T", CultureInfo.GetCultureInfo("pt-BR")), row.TimeText);
        Assert.Equal("Aviso", row.SeverityText);
        Assert.Equal(MetricState.Warning, row.Tone);
        Assert.Equal("SafetyMonitor", row.Source);
        Assert.True(row.IsProblem);
        Assert.False(row.HasDetails);
        Assert.Contains("Aviso", row.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("SafetyMonitor: Teste interrompido", row.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LogSeverity.Verbose, MetricState.Neutral)]
    [InlineData(LogSeverity.Information, MetricState.Neutral)]
    [InlineData(LogSeverity.Warning, MetricState.Warning)]
    [InlineData(LogSeverity.Error, MetricState.Critical)]
    [InlineData(LogSeverity.Fatal, MetricState.Critical)]
    public void EachSeverityHasItsTone(LogSeverity severity, MetricState tone)
    {
        Assert.Equal(tone, new LogEntryViewModel(Entry(0, severity, "x")).Tone);
    }

    [Fact]
    public void TheSummaryCountsEntriesWarningsAndErrorsIncludingFatal()
    {
        var entries = new[]
        {
            Entry(0, LogSeverity.Information, "a"),
            Entry(1, LogSeverity.Warning, "b"),
            Entry(2, LogSeverity.Warning, "c"),
            Entry(3, LogSeverity.Error, "d"),
            Entry(4, LogSeverity.Fatal, "e"),
        };

        Assert.Equal("5 entradas · 2 avisos · 2 erros", LogRows.Summary(entries));
    }

    [Fact]
    public void ThePlainTextKeepsTheOrderOnScreenAndTheDetails()
    {
        var shown = LogRows.Filter(
        [
            Entry(0, LogSeverity.Information, "primeiro"),
            Entry(1, LogSeverity.Error, "segundo", details: "stack"),
        ], 0, string.Empty, out _);

        var text = LogRows.PlainText(shown);

        var lines = text.Split(Environment.NewLine);
        Assert.Contains("[Error] M1vClock.App.App: segundo", lines[0], StringComparison.Ordinal);
        Assert.Equal("stack", lines[1]);
        Assert.Contains("primeiro", lines[2], StringComparison.Ordinal);
    }
}

public sealed class LogsPageViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    private sealed class StubLogs : ILogService
    {
        public ReadResult<IReadOnlyList<LogFile>> Files { get; set; } = ReadResult.Success<IReadOnlyList<LogFile>>([]);

        public Dictionary<string, ReadResult<LogSlice>> Slices { get; } = [];

        public List<string> Reads { get; } = [];

        public Task<ReadResult<IReadOnlyList<LogFile>>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(Files);

        public Task<ReadResult<LogSlice>> ReadAsync(LogFile file, CancellationToken cancellationToken)
        {
            lock (Reads)
            {
                Reads.Add(file.Name);
            }

            return Task.FromResult(Slices[file.Name]);
        }

        public async Task WaitForReadsAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                lock (Reads)
                {
                    if (Reads.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(10, timeout.Token);
            }
        }
    }

    private sealed class RecordingShell : IShellLauncher
    {
        public List<string> Folders { get; } = [];

        public void OpenUrl(Uri url) => throw new NotSupportedException();

        public void OpenFolder(string path) => Folders.Add(path);

        public void Start(string fileName, string? arguments, string? workingDirectory) => throw new NotSupportedException();
    }

    private sealed class StubClipboard(bool works) : IClipboardService
    {
        public string? Text { get; private set; }

        public bool TrySetText(string text)
        {
            Text = text;
            return works;
        }
    }

    private static LogFile File(string name, DateTimeOffset modified) => new(@"C:\logs\" + name, name, 1000, modified);

    private static LogSlice Slice(LogFile file, bool mid, params DiagLogEntry[] entries) => new(file, entries, mid);

    private static DiagLogEntry Entry(int minute, LogSeverity severity, string message) => new(Now.AddMinutes(minute), severity, "M1vClock.App.App", message, null);

    private static (LogsPageViewModel Page, StubLogs Logs, RecordingShell Shell, StubClipboard Clipboard) Create(bool clipboardWorks = true)
    {
        var logs = new StubLogs();
        var shell = new RecordingShell();
        var clipboard = new StubClipboard(clipboardWorks);
        var clock = new FakeTimeProvider(Now);
        var page = new LogsPageViewModel(logs, shell, clipboard, new AppPaths(@"C:\dados\m1vClock", false), clock, new CollectingLogger<LogsPageViewModel>());
        return (page, logs, shell, clipboard);
    }

    private static StubLogs TwoDays(StubLogs logs)
    {
        var today = File("m1vclock-20261022.log", Now);
        var yesterday = File("m1vclock-20261021.log", Now.AddDays(-1));
        logs.Files = ReadResult.Success<IReadOnlyList<LogFile>>([today, yesterday]);
        logs.Slices[today.Name] = ReadResult.Success(Slice(today, false, Entry(0, LogSeverity.Information, "inicio"), Entry(1, LogSeverity.Warning, "aviso hoje"), Entry(2, LogSeverity.Error, "erro hoje")));
        logs.Slices[yesterday.Name] = ReadResult.Success(Slice(yesterday, true, Entry(-1440, LogSeverity.Information, "ontem")));
        return logs;
    }

    [Fact]
    public async Task TheNewestFileIsOpenedFirstAndItsEntriesComeNewestFirst()
    {
        var (page, logs, _, _) = Create();
        TwoDays(logs);

        await page.RefreshAsync();

        Assert.Equal(["Hoje · 1000 B", "Ontem · 1000 B"], page.FileLabels);
        Assert.Equal(0, page.FileIndex);
        Assert.Equal(["erro hoje", "aviso hoje", "inicio"], page.Entries.Select(e => e.Message));
        Assert.Equal("3 entradas · 1 avisos · 1 erros", page.SummaryText);
        Assert.False(page.HasNotice);
        Assert.False(page.IsLoading);
        Assert.Equal(["m1vclock-20261022.log"], logs.Reads);
    }

    [Fact]
    public async Task OnlySevenFilesAreOfferedEvenWhenThereAreMore()
    {
        var (page, logs, _, _) = Create();
        var files = Enumerable.Range(0, 10).Select(i => File($"m1vclock-2026100{i % 10}.log", Now.AddDays(-i))).ToList();
        logs.Files = ReadResult.Success<IReadOnlyList<LogFile>>(files);
        foreach (var f in files)
        {
            logs.Slices[f.Name] = ReadResult.Success(Slice(f, false));
        }

        await page.RefreshAsync();

        Assert.Equal(LogsPageViewModel.FilesShown, page.FileLabels.Count);
    }

    [Fact]
    public async Task ChoosingAnotherFileReadsItAndAWarningAboutTheCutShowsForABigOne()
    {
        var (page, logs, _, _) = Create();
        TwoDays(logs);
        await page.RefreshAsync();

        page.FileIndex = 1;
        await logs.WaitForReadsAsync(2);
        await WaitUntilAsync(() => page.Entries.Count == 1);

        Assert.Equal("m1vclock-20261021.log", logs.Reads[1]);
        Assert.Equal(["ontem"], page.Entries.Select(e => e.Message));
        Assert.True(page.HasNotice);
        Assert.Equal("O arquivo é grande; só o final dele foi lido.", page.Notice);
        Assert.Equal(1, page.FileIndex);
    }

    [Fact]
    public async Task TheLevelFilterAndTheSearchNarrowTheListWithoutReadingTheFileAgain()
    {
        var (page, logs, _, _) = Create();
        TwoDays(logs);
        await page.RefreshAsync();

        page.LevelIndex = 1;
        Assert.Equal(["erro hoje", "aviso hoje"], page.Entries.Select(e => e.Message));
        page.SearchText = "aviso";
        Assert.Equal(["aviso hoje"], page.Entries.Select(e => e.Message));
        page.SearchText = "nada";
        Assert.Empty(page.Entries);
        Assert.True(page.ShowEmpty);
        Assert.Single(logs.Reads);
    }

    [Fact]
    public async Task NoLogFilesYetShowsTheFriendlyMessageNotAnError()
    {
        var (page, _, _, _) = Create();

        await page.RefreshAsync();

        Assert.True(page.ShowNoFiles);
        Assert.False(page.HasProblems);
        Assert.False(page.HasFiles);
        Assert.False(page.ShowEmpty);
    }

    [Fact]
    public async Task AFolderThatCannotBeListedShowsTheProblemInsteadOfTheEmptyMessage()
    {
        var (page, logs, _, _) = Create();
        logs.Files = ReadResult.Failure<IReadOnlyList<LogFile>>("Acesso negado.");

        await page.RefreshAsync();

        Assert.True(page.HasProblems);
        Assert.Equal("Não foi possível ler os logs: Acesso negado.", page.Problems);
        Assert.False(page.ShowNoFiles);
    }

    [Fact]
    public async Task AFileThatCannotBeReadShowsTheProblemAndClearsTheList()
    {
        var (page, logs, _, _) = Create();
        TwoDays(logs);
        logs.Slices["m1vclock-20261022.log"] = ReadResult.Failure<LogSlice>("em uso");

        await page.RefreshAsync();

        Assert.Equal("Não foi possível ler os logs: em uso", page.Problems);
        Assert.Empty(page.Entries);
        Assert.False(page.HasFiles);
    }

    [Fact]
    public async Task TheFolderButtonOpensTheLogsFolderOfTheApp()
    {
        var (page, _, shell, _) = Create();

        page.OpenFolderCommand.Execute(null);
        await Task.CompletedTask;

        Assert.Equal([@"C:\dados\m1vClock\logs"], shell.Folders);
    }

    [Fact]
    public async Task CopyingPutsTheVisibleEntriesOnTheClipboardAndSaysSo()
    {
        var (page, logs, _, clipboard) = Create();
        TwoDays(logs);
        await page.RefreshAsync();
        page.LevelIndex = 2;

        page.CopyCommand.Execute(null);

        Assert.Contains("erro hoje", clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("aviso hoje", clipboard.Text, StringComparison.Ordinal);
        Assert.Equal("Copiado para a área de transferência.", page.Notice);
    }

    [Fact]
    public async Task ABusyClipboardIsReportedInsteadOfPretendingItWorked()
    {
        var (page, logs, _, _) = Create(clipboardWorks: false);
        TwoDays(logs);
        await page.RefreshAsync();

        page.CopyCommand.Execute(null);

        Assert.StartsWith("O Windows não deixou usar a área de transferência", page.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABigListShowsHowManyAreHiddenBeyondTheNewestThreeHundred()
    {
        var (page, logs, _, _) = Create();
        var file = File("m1vclock-20261022.log", Now);
        logs.Files = ReadResult.Success<IReadOnlyList<LogFile>>([file]);
        logs.Slices[file.Name] = ReadResult.Success(Slice(file, false, [.. Enumerable.Range(0, 450).Select(i => Entry(i, LogSeverity.Information, $"m{i}"))]));

        await page.RefreshAsync();

        Assert.Equal(LogRows.MaximumShown, page.Entries.Count);
        Assert.True(page.HasShowing);
        Assert.Equal("Mostrando as 300 mais recentes de 450 entradas que combinam com o filtro.", page.ShowingText);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}

public sealed class LogTextsTests
{
    private static readonly ResourceManager Resources = new("M1vClock.App.Localization.Strings", typeof(Loc).Assembly);

    public static TheoryData<string> Languages => ["pt-BR", "en-US"];

    [Theory]
    [MemberData(nameof(Languages))]
    public void EverySeverityOfTheLogHasItsText(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        var missing = Enum.GetValues<LogSeverity>().Select(s => $"Diag.Logs.Severity.{s}").Where(k => string.IsNullOrWhiteSpace(Resources.GetString(k, culture))).ToList();

        Assert.True(missing.Count == 0, "faltam: " + string.Join(", ", missing));
    }
}
