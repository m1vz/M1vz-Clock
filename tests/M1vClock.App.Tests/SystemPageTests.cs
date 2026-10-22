using M1vClock.App.Formatting;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Privacy;
using M1vClock.Core.Results;
using M1vClock.Hardware.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.App.Tests;

public sealed class SystemRowsTests
{
    private static readonly OsInfo Os = new("Windows 11 Home", "10.0.26200", "26200", "25H2", "9711", "64 bits", "DESKTOP-TLC691T");
    private static readonly CpuInfo Cpu = new("AMD Ryzen 7 5700X 8-Core Processor", "AMD", "AM4", 8, 16, 3401, 4096, 32768, true);

    [Fact]
    public void OperatingSystemRowsCombineDisplayVersionAndBuild()
    {
        var rows = SystemRows.Os(Os);

        Assert.Equal(["Nome", "Versão", "Build", "Arquitetura", "Nome do computador"], rows.Select(r => r.Label));
        Assert.Equal(["Windows 11 Home", "25H2 (10.0.26200)", "26200.9711", "64 bits", "DESKTOP-TLC691T"], rows.Select(r => r.Value));
    }

    [Fact]
    public void WithoutADisplayVersionTheKernelVersionIsShown()
    {
        var rows = SystemRows.Os(Os with { DisplayVersion = null, Revision = null });

        Assert.Equal("10.0.26200", rows[1].Value);
        Assert.Equal("26200", rows[2].Value);
    }

    [Fact]
    public void OnlyTheBoardSerialIsSensitive()
    {
        var rows = SystemRows.Board(new BoardInfo("ASUSTeK COMPUTER INC.", "TUF GAMING B550M-PLUS", "Rev X.0x", new SensitiveText("260401253302497")));

        Assert.Equal([false, false, false, true], rows.Select(r => r.IsSensitive));
        var serial = rows[3];
        Assert.True(serial.CanReveal);
        Assert.Equal(SensitiveText.Mask, serial.Display);
        Assert.DoesNotContain("2604", serial.Display, StringComparison.Ordinal);
    }

    [Fact]
    public void ASerialThatIsMissingHasNothingToReveal()
    {
        var rows = SystemRows.Board(new BoardInfo("MSI", "B650", null, new SensitiveText(null)));

        Assert.False(rows[3].CanReveal);
        Assert.Equal(Units.Missing, rows[3].Display);
        Assert.Equal(Units.Missing, rows[2].Value);
    }

    [Fact]
    public void RevealingTheSerialShowsItAndHidingMasksItAgain()
    {
        var serial = SystemRows.Board(new BoardInfo("ASUS", "TUF", null, new SensitiveText("ABC123")))[3];
        var notified = new List<string?>();
        serial.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        serial.ToggleRevealCommand.Execute(null);

        Assert.Equal("ABC123", serial.Display);
        Assert.Equal("Ocultar", serial.RevealToolTip);
        Assert.Contains(nameof(InfoRowViewModel.Display), notified);

        serial.ToggleRevealCommand.Execute(null);

        Assert.Equal(SensitiveText.Mask, serial.Display);
        Assert.Equal("Mostrar", serial.RevealToolTip);
    }

    [Fact]
    public void BiosRowsShowTheDateInTheCurrentCulture()
    {
        var rows = SystemRows.Bios(new BiosInfo("American Megatrends Inc.", "3636", new DateOnly(2026, 1, 4)));

        Assert.Equal("American Megatrends Inc.", rows[0].Value);
        Assert.Equal("3636", rows[1].Value);
        Assert.Equal("04/01/2026", rows[2].Value);
    }

    [Fact]
    public void ProcessorRowsUseHumanUnits()
    {
        var rows = SystemRows.Cpu(Cpu);

        Assert.Equal(["AMD", "AM4", "8", "16", "3,40 GHz", "4 MB", "32 MB", "Sim"], rows.Select(r => r.Value));
    }

    [Fact]
    public void UnknownProcessorFieldsShowADash()
    {
        var rows = SystemRows.Cpu(new CpuInfo("CPU", "Intel", null, 0, 0, 0, 0, 0, null));

        Assert.Equal(["Intel", "—", "—", "—", "—", "—", "—", "—"], rows.Select(r => r.Value));
    }
}

public sealed class InfoSectionViewModelTests
{
    [Fact]
    public void StartsLoadingWithNoRows()
    {
        var section = new InfoSectionViewModel("Sistema operacional", "Icon.System");

        Assert.True(section.IsLoading);
        Assert.False(section.IsReady);
        Assert.False(section.IsFailed);
        Assert.Empty(section.Rows);
    }

    [Fact]
    public void SetReadyShowsRowsAndNotifiesTheStateFlags()
    {
        var section = new InfoSectionViewModel("Sistema", "Icon.System");
        var changed = new List<string?>();
        section.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        section.SetReady([new InfoRowViewModel("Nome", "Windows")]);

        Assert.True(section.IsReady);
        Assert.False(section.IsLoading);
        Assert.Single(section.Rows);
        Assert.Contains(nameof(InfoSectionViewModel.IsReady), changed);
        Assert.Contains(nameof(InfoSectionViewModel.IsLoading), changed);
    }

    [Fact]
    public void SetFailedClearsRowsAndExplainsTheReason()
    {
        var section = new InfoSectionViewModel("BIOS", "Icon.Bios");
        section.SetReady([new InfoRowViewModel("a", "b")]);

        section.SetFailed("Acesso negado.");

        Assert.True(section.IsFailed);
        Assert.Empty(section.Rows);
        Assert.Equal("Acesso negado.", section.Error);
        Assert.Equal("Motivo: Acesso negado.", section.ErrorDetail);
    }

    [Fact]
    public void SetLoadingResetsAFailedSection()
    {
        var section = new InfoSectionViewModel("BIOS", "Icon.Bios");
        section.SetFailed("erro");

        section.SetLoading();

        Assert.True(section.IsLoading);
        Assert.Equal(string.Empty, section.Error);
    }

    [Fact]
    public void ApplyMapsSuccessesAndFailures()
    {
        var ok = new InfoSectionViewModel("a", "Icon.System");
        var broken = new InfoSectionViewModel("b", "Icon.System");

        ok.Apply(ReadResult.Success("valor"), v => [new InfoRowViewModel("Campo", v)]);
        broken.Apply(ReadResult.Failure<string>("sem WMI"), v => [new InfoRowViewModel("Campo", v)]);

        Assert.Equal("valor", Assert.Single(ok.Rows).Value);
        Assert.True(broken.IsFailed);
        Assert.Equal("sem WMI", broken.Error);
    }

    [Fact]
    public void ARowThatIsNotSensitiveNeverMasksOrOffersToReveal()
    {
        var row = new InfoRowViewModel("Modelo", "TUF");

        Assert.False(row.CanReveal);
        Assert.Equal("TUF", row.Display);
    }
}

public sealed class SystemPageViewModelTests
{
    private static readonly PlatformReport Healthy = new(
        ReadResult.Success(new OsInfo("Windows 11 Home", "10.0.26200", "26200", "25H2", "9711", "64 bits", "PC")),
        ReadResult.Success(new BoardInfo("ASUS", "TUF", "Rev", new SensitiveText("S1"))),
        ReadResult.Success(new BiosInfo("AMI", "3636", new DateOnly(2026, 1, 4))),
        ReadResult.Success<IReadOnlyList<CpuInfo>>([new CpuInfo("AMD Ryzen 7 5700X 8-Core Processor", "AMD", "AM4", 8, 16, 3401, 4096, 32768, true)]));

    [Fact]
    public async Task LoadsEverySectionWithTheProcessorNamedByItsModel()
    {
        var page = Create(_ => Task.FromResult(Healthy), out _);

        await page.RefreshAsync();

        Assert.Equal(4, page.Sections.Count);
        Assert.All(page.Sections, s => Assert.True(s.IsReady, s.Title));
        Assert.Equal(["Sistema operacional", "Placa-mãe", "BIOS", "AMD Ryzen 7 5700X 8-Core Processor"], page.Sections.Select(s => s.Title));
        Assert.False(page.HasPageError);
        Assert.StartsWith("Atualizado às", page.UpdatedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneFailedSectionDoesNotHideTheOthers()
    {
        var report = Healthy with { Bios = ReadResult.Failure<BiosInfo>("BIOS ilegível.") };
        var page = Create(_ => Task.FromResult(report), out _);

        await page.RefreshAsync();

        Assert.True(page.Sections[0].IsReady);
        Assert.True(page.Sections[2].IsFailed);
        Assert.Equal("BIOS ilegível.", page.Sections[2].Error);
        Assert.True(page.Sections[3].IsReady);
    }

    [Fact]
    public async Task AFailedProcessorReadShowsASingleFailedProcessorSection()
    {
        var report = Healthy with { Cpus = ReadResult.Failure<IReadOnlyList<CpuInfo>>("Sem processador.") };
        var page = Create(_ => Task.FromResult(report), out _);

        await page.RefreshAsync();

        var cpu = page.Sections[3];
        Assert.Equal("Processador", cpu.Title);
        Assert.True(cpu.IsFailed);
        Assert.Equal(4, page.Sections.Count);
    }

    [Fact]
    public async Task RefreshingTwiceDoesNotDuplicateProcessors()
    {
        var page = Create(_ => Task.FromResult(Healthy), out _);

        await page.RefreshAsync();
        await page.RefreshAsync();

        Assert.Equal(4, page.Sections.Count);
    }

    [Fact]
    public async Task EverySectionRetriesWithTheRefreshCommand()
    {
        var page = Create(_ => Task.FromResult(Healthy), out _);
        await page.RefreshAsync();

        Assert.All(page.Sections, s => Assert.Same(page.RefreshCommand, s.RetryCommand));
    }

    [Fact]
    public async Task AnUnexpectedErrorIsLoggedAndShownAsAPageError()
    {
        var page = Create(_ => throw new FormatException("bug"), out var logger);

        await page.RefreshAsync();

        Assert.True(page.HasPageError);
        Assert.Equal("Algo deu errado ao ler esta página. O erro foi gravado no log.", page.PageError);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(2016, entry.EventId.Id);
        Assert.IsType<FormatException>(entry.Exception);
    }

    [Fact]
    public async Task LeavingThePageCancelsTheReadAndShowsNothingStale()
    {
        var gate = new TaskCompletionSource<PlatformReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = Create(_ => gate.Task, out _);

        var refresh = page.RefreshAsync();
        page.OnNavigatedFrom();
        gate.SetResult(Healthy);
        await refresh;

        Assert.All(page.Sections, s => Assert.True(s.IsLoading, s.Title));
        Assert.False(page.HasPageError);
    }

    [Fact]
    public async Task ASecondRefreshSupersedesTheFirst()
    {
        var first = new TaskCompletionSource<PlatformReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var page = Create(_ => ++calls == 1 ? first.Task : Task.FromResult(Healthy), out _);

        var slow = page.RefreshAsync();
        await page.RefreshAsync();
        first.SetResult(Healthy with { Os = ReadResult.Failure<OsInfo>("antigo") });
        await slow;

        Assert.True(page.Sections[0].IsReady);
    }

    private static SystemPageViewModel Create(Func<CancellationToken, Task<PlatformReport>> read, out CollectingLogger<SystemPageViewModel> logger)
    {
        logger = new CollectingLogger<SystemPageViewModel>();
        return new SystemPageViewModel(new StubPlatform(read), logger);
    }

    private sealed class StubPlatform(Func<CancellationToken, Task<PlatformReport>> read) : IPlatformService
    {
        public Task<PlatformReport> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);
    }
}

internal sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);

internal sealed class CollectingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => NullLogger.Instance.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }
}
