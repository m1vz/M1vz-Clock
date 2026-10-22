using System.Globalization;
using System.IO;
using System.Resources;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Diagnostics;
using M1vClock.App.Pages.Performance;
using M1vClock.App.Services;
using M1vClock.App.Services.Reports;
using M1vClock.Benchmarks.Results;
using M1vClock.Core.Privacy;
using M1vClock.Core.Results;
using M1vClock.Diagnostics.Crashes;
using M1vClock.Diagnostics.Events;
using M1vClock.Diagnostics.Reliability;
using M1vClock.Hardware.Platform;

namespace M1vClock.App.Tests;

public sealed class ReportRendererTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 19, 1, 0, TimeSpan.Zero);

    private static ReportDocument Document(string? name = null, params ReportSection[] sections) => new(At, "0.1.0", name, sections);

    private static ReportSection Section(string id, string title, params (string Label, string Value)[] rows) =>
        new(id, title, rows.Select(r => new ReportRow(r.Label, r.Value)).ToList(), []);

    private static HashSet<string> Only(params string[] ids) => ids.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void TheTextReportHasATitleAHeaderAndOneBlockPerChosenSection()
    {
        var document = Document(null, Section("system", "Sistema", ("Nome", "Windows 11 Home")), Section("gpu", "Placa de vídeo", ("Chip", "Navi 14")));

        var text = ReportRenderer.Text(document, Only("system", "gpu"));

        var lines = text.Split(Environment.NewLine);
        Assert.Equal("Relatório do m1vClock", lines[0]);
        Assert.Equal("Gerado em " + At.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("pt-BR")) + " · m1vClock 0.1.0", lines[1]);
        Assert.Contains("## Sistema", lines, StringComparer.Ordinal);
        Assert.Contains("- Nome: Windows 11 Home", lines, StringComparer.Ordinal);
        Assert.Contains("## Placa de vídeo", lines, StringComparer.Ordinal);
        Assert.Contains("Gerado neste computador pelo m1vClock. Nada foi enviado para a internet.", lines, StringComparer.Ordinal);
    }

    [Fact]
    public void OnlyTheChosenSectionsAppearAndEmptyOnesAreSkipped()
    {
        var document = Document(null, Section("system", "Sistema", ("Nome", "x")), Section("gpu", "Placa de vídeo", ("Chip", "y")), new ReportSection("power", "Energia", [], []));

        var text = ReportRenderer.Text(document, Only("system", "power"));

        Assert.Contains("## Sistema", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Placa de vídeo", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Energia", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheComputerNameShowsInTheHeaderOnlyWhenTheDocumentCarriesIt()
    {
        var section = Section("system", "Sistema", ("Nome", "x"));

        Assert.DoesNotContain("DESKTOP-TLC691T", ReportRenderer.Text(Document(null, section), Only("system")), StringComparison.Ordinal);
        Assert.Contains("· DESKTOP-TLC691T", ReportRenderer.Text(Document("DESKTOP-TLC691T", section), Only("system")), StringComparison.Ordinal);
        Assert.DoesNotContain("DESKTOP", ReportRenderer.Header(Document("   ")), StringComparison.Ordinal);
    }

    [Fact]
    public void NotesComeAfterTheRowsOfTheirSection()
    {
        var section = new ReportSection("storage", "Armazenamento", [new ReportRow("Disco", "Saudável")], ["O SMART não pôde ser lido sem administrador."]);

        var lines = ReportRenderer.Text(Document(null, section), Only("storage")).Split(Environment.NewLine);

        Assert.True(Array.IndexOf(lines, "- Disco: Saudável") < Array.IndexOf(lines, "O SMART não pôde ser lido sem administrador."));
    }

    [Fact]
    public void TheHtmlReportEscapesEverythingTheMachineCanPutInIt()
    {
        var nasty = "<script>alert(1)</script> & \"aspas\"";
        var document = Document("PC<x>", Section("system", "Sistema <b>", (nasty, nasty)));

        var html = ReportRenderer.Html(document, Only("system"));

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;aspas&quot;", html, StringComparison.Ordinal);
        Assert.Contains("PC&lt;x&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHtmlReportIsACompleteStandalonePageWithNothingFromTheNetwork()
    {
        var html = ReportRenderer.Html(Document(null, Section("system", "Sistema", ("Nome", "x"))), Only("system"));

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"pt-BR\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
        Assert.Contains("<title>Relatório do m1vClock</title>", html, StringComparison.Ordinal);
        Assert.Contains("prefers-color-scheme:dark", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<dt>Nome</dt><dd>x</dd>", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>\n", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSuggestedFileNameHasTheLocalDateAndTime()
    {
        Assert.Equal("relatorio-m1vclock-" + At.ToLocalTime().ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture), ReportRenderer.FileName(At));
    }
}

public sealed class ReportSectionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    private static PlatformReport Platform() => new(
        ReadResult.Success(new OsInfo("Windows 11 Home", "10.0.26200", "26200", "25H2", "9711", "64 bits", "DESKTOP-TLC691T")),
        ReadResult.Success(new BoardInfo("ASUSTeK COMPUTER INC.", "TUF GAMING B550M-PLUS", "Rev X.0x", new SensitiveText("260401253302497"))),
        ReadResult.Success(new BiosInfo("American Megatrends Inc.", "3636", new DateOnly(2026, 1, 4))),
        ReadResult.Success<IReadOnlyList<CpuInfo>>([new CpuInfo("AMD Ryzen 7 5700X 8-Core Processor", "AMD", "AM4", 8, 16, 3400, 4096, 32768, true)]));

    [Fact]
    public void TheSystemSectionNeverCarriesTheComputerNameOrTheBoardSerial()
    {
        var section = ReportSections.System(Platform());

        var all = string.Join('\n', section.Rows.Select(r => $"{r.Label}={r.Value}"));
        Assert.DoesNotContain("DESKTOP-TLC691T", all, StringComparison.Ordinal);
        Assert.DoesNotContain("260401253302497", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Nome do computador", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Número de série", all, StringComparison.Ordinal);
        Assert.Contains(section.Rows, r => r.Label == "Modelo" && r.Value == "TUF GAMING B550M-PLUS");
        Assert.Equal("Sistema", section.Title);
        Assert.Empty(section.Notes);
    }

    [Fact]
    public void APartThatCouldNotBeReadBecomesANoteInsteadOfBreakingTheSection()
    {
        var platform = Platform() with { Bios = ReadResult.Failure<BiosInfo>("WMI quebrado"), Cpus = ReadResult.Failure<IReadOnlyList<CpuInfo>>("sem CPU") };

        var section = ReportSections.System(platform);

        Assert.Equal(["Não foi possível ler esta parte: WMI quebrado", "Não foi possível ler esta parte: sem CPU"], section.Notes);
        Assert.Contains(section.Rows, r => r.Value == "Windows 11 Home");
    }

    [Fact]
    public void TheEventsSectionSummarisesTheCountsAndListsOnlyTheRealProblems()
    {
        IEnumerable<RawEvent> Repeat(string provider, int id, EventSeverity severity, int count) =>
            Enumerable.Range(0, count).Select(i => new RawEvent("System", provider, id, severity, Now.AddMinutes(-i), null));
        var groups = EventService.Group([.. Repeat("ESENT", 490, EventSeverity.Error, 1500), .. Repeat("Microsoft-Windows-Kernel-Power", 41, EventSeverity.Critical, 4), .. Repeat("winsrvext", 100, EventSeverity.Warning, 61)]);
        var report = new EventReport(TimeSpan.FromDays(30), groups, new Dictionary<string, string>(), false);

        var section = ReportSections.Events(report);

        Assert.Equal(("Críticos", "4"), (section.Rows[0].Label, section.Rows[0].Value));
        Assert.Equal("1.500", section.Rows[1].Value);
        Assert.Equal("61", section.Rows[2].Value);
        var listed = section.Rows.Skip(3).ToList();
        Assert.Equal(2, listed.Count);
        Assert.StartsWith("O PC desligou sem avisar (Microsoft-Windows-Kernel-Power 41)", listed[0].Label, StringComparison.Ordinal);
        Assert.DoesNotContain(listed, r => r.Label.Contains("ESENT", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCrashesSectionTellsWhenDumpsAreOffAndListsTheRestartsAndTheDriverCrash()
    {
        var report = new CrashReport(
            TimeSpan.FromDays(30),
            ReadResult.Success(new DumpSettings(DumpMode.Disabled, @"C:\WINDOWS\Minidump", @"C:\WINDOWS\MEMORY.DMP", false, false)),
            [new UnexpectedShutdown(Now.AddDays(-3), ShutdownCause.PowerButton, null), new UnexpectedShutdown(Now.AddDays(-6), ShutdownCause.Unknown, null)],
            [new AppCrashGroup("dwm.exe", "amdxx64.dll", 0xC0000005, FaultOrigin.GraphicsDriver, "AMD", 1, Now.AddDays(-1), Now.AddDays(-1))],
            [],
            [new DumpFileInfo(@"C:\Users\joaop\AppData\Local\CrashDumps\x.dmp", "x.dmp", 10, Now, null)],
            new Dictionary<string, string>());

        var section = ReportSections.Crashes(report);

        var all = string.Join('\n', section.Rows.Select(r => $"{r.Label}={r.Value}"));
        Assert.Contains("Tipo de dump=Desligado (nada é guardado)", all, StringComparison.Ordinal);
        Assert.Contains("Reinícios inesperados=2", all, StringComparison.Ordinal);
        Assert.Contains("Desligamento forçado pelo botão de energia", all, StringComparison.Ordinal);
        Assert.Contains("dwm.exe travou em amdxx64.dll", all, StringComparison.Ordinal);
        Assert.Single(section.Notes);
        Assert.Contains("não guardar nada", section.Notes[0], StringComparison.Ordinal);
        Assert.DoesNotContain("joaop", all, StringComparison.Ordinal);
        Assert.DoesNotContain("CrashDumps", all, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReliabilitySectionGivesTheIndexTheLowestAndTheFailures()
    {
        var report = new ReliabilityReport(
            TimeSpan.FromDays(30),
            ReadResult.Success<IReadOnlyList<StabilityPoint>>([new StabilityPoint(Now.AddHours(-1), 5.842), new StabilityPoint(Now, 7.05)]),
            ReadResult.Success<IReadOnlyList<ReliabilityRecord>>([new ReliabilityRecord(Now, "EventLog", 6008, "Windows", ReliabilityKind.UnexpectedShutdown)]));

        var section = ReportSections.Reliability(report);

        Assert.Equal([("Agora", "7,05 · Falhas ocasionais"), ("Mais baixo no período", "5,84"), ("Falhas", "1")], section.Rows.Select(r => (r.Label, r.Value)));
    }

    [Fact]
    public void TheBenchmarksSectionShowsTheLatestResultOfEachTestThatWasRun()
    {
        var machine = new BenchmarkMachine("CPU", "GPU", 16, 16, "10.0.26200", "0.1.0");
        BenchmarkRecord Record(string test, double score, int daysAgo) => new() { Id = Guid.NewGuid(), At = Now.AddDays(-daysAgo), Test = test, ScoreVersion = 1, Score = score, Metrics = new Dictionary<string, double>(), Machine = machine };

        var section = ReportSections.Benchmarks([Record(BenchmarkTests.CpuMulti, 2748, 1), Record(BenchmarkTests.CpuMulti, 3300, 5), Record(BenchmarkTests.Memory, 35.6, 2)]);

        Assert.Equal(2, section.Rows.Count);
        Assert.Contains(section.Rows, r => r.Label == "Render de CPU · multi-core" && r.Value.StartsWith("2.748", StringComparison.Ordinal));
        Assert.DoesNotContain(section.Rows, r => r.Value.StartsWith("3.300", StringComparison.Ordinal));
    }

    [Fact]
    public void NoBenchmarkYetMeansAnEmptySectionThatTheReportSkips()
    {
        Assert.True(ReportSections.Benchmarks([]).IsEmpty);
    }
}

public sealed class ReportPageViewModelTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 19, 1, 0, TimeSpan.Zero);

    private sealed class StubCollector(Func<Task<ReportDocument>> collect) : IReportCollector
    {
        public int Collections { get; private set; }

        public Task<ReportDocument> CollectAsync(CancellationToken cancellationToken)
        {
            Collections++;
            return collect();
        }
    }

    private sealed class StubDialogs(string? answer) : IFileDialogs
    {
        public List<(string Title, string Filter, string Suggested)> Saves { get; } = [];

        public string? PickOpen(string title, string filter) => throw new NotSupportedException();

        public string? PickSave(string title, string filter, string suggestedName)
        {
            Saves.Add((title, filter, suggestedName));
            return answer;
        }
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

    private static ReportDocument Document() => new(
        At,
        "0.1.0",
        null,
        [
            new ReportSection("system", "Sistema", [new ReportRow("Nome", "Windows 11 Home")], []),
            new ReportSection("gpu", "Placa de vídeo", [new ReportRow("Chip", "Navi 14")], []),
            new ReportSection("power", "Energia", [], []),
        ]);

    private static ReportPageViewModel Create(IReportCollector collector, IFileDialogs dialogs, IClipboardService clipboard) =>
        new(collector, dialogs, clipboard, new CollectingLogger<ReportPageViewModel>(), () => "DESKTOP-TLC691T");

    [Fact]
    public async Task EverySectionThatHasDataStartsIncludedAndAnEmptyOneStartsOutAndDisabled()
    {
        var page = Create(new StubCollector(() => Task.FromResult(Document())), new StubDialogs(null), new StubClipboard(true));

        await page.RefreshAsync();

        Assert.Equal(["system", "gpu", "power"], page.Parts.Select(p => p.Id));
        Assert.Equal([true, true, false], page.Parts.Select(p => p.Include));
        Assert.Equal([true, true, false], page.Parts.Select(p => p.HasData));
        Assert.Equal("1 linha(s)", page.Parts[0].RowsText);
        Assert.True(page.CanExport);
        Assert.False(page.IsLoading);
        Assert.Contains("## Sistema", page.Preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Energia", page.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurningASectionOffRemovesItFromThePreviewAndTurningEverythingOffLeavesNothingToExport()
    {
        var page = Create(new StubCollector(() => Task.FromResult(Document())), new StubDialogs(null), new StubClipboard(true));
        await page.RefreshAsync();

        page.Parts[1].Include = false;
        Assert.DoesNotContain("Placa de vídeo", page.Preview, StringComparison.Ordinal);
        Assert.True(page.CanExport);

        page.Parts[0].Include = false;
        Assert.Equal(string.Empty, page.Preview);
        Assert.False(page.CanExport);
        Assert.True(page.ShowNothingChosen);
        Assert.False(page.HasPreview);
    }

    [Fact]
    public async Task TheComputerNameOnlyEntersTheReportWhenTheOptionIsOn()
    {
        var page = Create(new StubCollector(() => Task.FromResult(Document())), new StubDialogs(null), new StubClipboard(true));
        await page.RefreshAsync();
        Assert.DoesNotContain("DESKTOP-TLC691T", page.Preview, StringComparison.Ordinal);

        page.IncludeName = true;
        Assert.Contains("· DESKTOP-TLC691T", page.Preview, StringComparison.Ordinal);

        page.IncludeName = false;
        Assert.DoesNotContain("DESKTOP-TLC691T", page.Preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingAsHtmlWritesAStandalonePageAndSaysWhere()
    {
        var path = Path.Combine(Path.GetTempPath(), "m1vclock-report-" + Path.GetRandomFileName() + ".html");
        var dialogs = new StubDialogs(path);
        var page = Create(new StubCollector(() => Task.FromResult(Document())), dialogs, new StubClipboard(true));
        await page.RefreshAsync();
        try
        {
            await page.SaveAsync();

            var html = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
            Assert.Contains("<dd>Windows 11 Home</dd>", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Energia", html, StringComparison.Ordinal);
            Assert.Equal("Relatório salvo em " + path + ".", page.Notice);
            Assert.False(page.HasProblem);
            var save = Assert.Single(dialogs.Saves);
            Assert.StartsWith("relatorio-m1vclock-", save.Suggested, StringComparison.Ordinal);
            Assert.Contains("*.html", save.Filter, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SavingWithATxtNameWritesThePlainTextVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), "m1vclock-report-" + Path.GetRandomFileName() + ".txt");
        var page = Create(new StubCollector(() => Task.FromResult(Document())), new StubDialogs(path), new StubClipboard(true));
        await page.RefreshAsync();
        try
        {
            await page.SaveAsync();

            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.StartsWith("Relatório do m1vClock", text, StringComparison.Ordinal);
            Assert.Contains("- Chip: Navi 14", text, StringComparison.Ordinal);
            Assert.DoesNotContain("<html", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CancellingTheSaveDialogChangesNothing()
    {
        var page = Create(new StubCollector(() => Task.FromResult(Document())), new StubDialogs(null), new StubClipboard(true));
        await page.RefreshAsync();

        await page.SaveAsync();

        Assert.False(page.HasNotice);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task APathThatCannotBeWrittenShowsTheProblemInsteadOfCrashing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "m1vclock-nao-existe-" + Path.GetRandomFileName());
        var page = Create(new StubCollector(() => Task.FromResult(Document())), new StubDialogs(Path.Combine(folder, "r.html")), new StubClipboard(true));
        await page.RefreshAsync();

        await page.SaveAsync();

        Assert.True(page.HasProblem);
        Assert.StartsWith("Não foi possível salvar o relatório:", page.Problem, StringComparison.Ordinal);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task CopyingPutsTheChosenSectionsAsTextOnTheClipboard()
    {
        var clipboard = new StubClipboard(true);
        var page = Create(new StubCollector(() => Task.FromResult(Document())), new StubDialogs(null), clipboard);
        await page.RefreshAsync();
        page.Parts[1].Include = false;

        page.CopyCommand.Execute(null);

        Assert.Contains("## Sistema", clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Placa de vídeo", clipboard.Text, StringComparison.Ordinal);
        Assert.Equal("Copiado para a área de transferência.", page.Notice);
    }

    [Fact]
    public async Task ABusyClipboardIsReportedAsAProblem()
    {
        var page = Create(new StubCollector(() => Task.FromResult(Document())), new StubDialogs(null), new StubClipboard(false));
        await page.RefreshAsync();

        page.CopyCommand.Execute(null);

        Assert.True(page.HasProblem);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task NothingIsSavedOrCopiedBeforeTheDataIsCollected()
    {
        var dialogs = new StubDialogs("x.html");
        var clipboard = new StubClipboard(true);
        var page = Create(new StubCollector(() => Task.FromResult(Document())), dialogs, clipboard);

        await page.SaveAsync();
        page.CopyCommand.Execute(null);

        Assert.Empty(dialogs.Saves);
        Assert.Null(clipboard.Text);
        Assert.False(page.CanExport);
    }

    [Fact]
    public async Task ACollectionThatBlowsUpShowsThePageErrorAndStopsLoading()
    {
        var page = Create(new StubCollector(() => throw new InvalidOperationException("quebrou")), new StubDialogs(null), new StubClipboard(true));

        await page.RefreshAsync();

        Assert.True(page.HasPageError);
        Assert.False(page.IsLoading);
        Assert.False(page.CanExport);
    }

    [Fact]
    public async Task CollectingAgainReplacesTheSectionsAndForgetsTheOldNotice()
    {
        var calls = 0;
        var collector = new StubCollector(() => Task.FromResult(calls++ == 0 ? Document() : Document() with { Sections = [new ReportSection("memory", "Memória", [new ReportRow("Total", "16 GB")], [])] }));
        var clipboard = new StubClipboard(true);
        var page = Create(collector, new StubDialogs(null), clipboard);
        await page.RefreshAsync();
        page.CopyCommand.Execute(null);
        Assert.True(page.HasNotice);

        await page.RefreshAsync();

        Assert.Equal(["memory"], page.Parts.Select(p => p.Id));
        Assert.False(page.HasNotice);
        Assert.Equal(2, collector.Collections);
    }
}

public sealed class ReportTextsTests
{
    private static readonly ResourceManager Resources = new("M1vClock.App.Localization.Strings", typeof(Loc).Assembly);

    public static TheoryData<string> Languages => ["pt-BR", "en-US"];

    [Theory]
    [MemberData(nameof(Languages))]
    public void EverySectionOfTheReportHasATitle(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);

        var missing = ReportSectionIds.All.Select(id => $"Diag.Report.Section.{id}").Where(k => string.IsNullOrWhiteSpace(Resources.GetString(k, culture))).ToList();

        Assert.True(missing.Count == 0, "faltam: " + string.Join(", ", missing));
    }

    [Fact]
    public void EverySectionIdIsUniqueAndLowercase()
    {
        Assert.Equal(ReportSectionIds.All.Count, ReportSectionIds.All.Distinct().Count());
        Assert.All(ReportSectionIds.All, id => Assert.Matches("^[a-z]+$", id));
    }
}

public sealed class BenchNamesForReportTests
{
    [Fact]
    public void TheCatalogNamesEveryTestTheReportCanList()
    {
        Assert.All(BenchmarkCatalog.All, info => Assert.False(BenchNames.Test(info.Id).StartsWith('[')));
    }
}
