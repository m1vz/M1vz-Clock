using System.Globalization;
using System.Resources;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Diagnostics;
using M1vClock.Core.Results;
using M1vClock.Diagnostics.Crashes;

namespace M1vClock.App.Tests;

public sealed class CrashRowsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    private static string Local(DateTimeOffset time) => time.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("pt-BR"));

    [Fact]
    public void ABlueScreenShowsItsNameItsExplanationAndItsParameters()
    {
        var shutdown = new UnexpectedShutdown(Now, ShutdownCause.BlueScreen, new BugCheckInfo(0x133, [1, 0x1E00, 0xFFFFF80012345678, 0]));

        var row = CrashRows.Shutdown(shutdown);

        Assert.Equal("Tela azul: DPC_WATCHDOG_VIOLATION", row.Title);
        Assert.StartsWith("Um driver demorou demais numa tarefa urgente", row.Detail, StringComparison.Ordinal);
        Assert.Equal("0x00000133 · Parâmetros: 0x1, 0x1E00, 0xFFFFF80012345678, 0x0", row.Meta);
        Assert.Equal("Crítico", row.Badge);
        Assert.Equal(MetricState.Critical, row.Tone);
        Assert.Equal(Local(Now), row.TrailingTop);
        Assert.Contains("Tela azul: DPC_WATCHDOG_VIOLATION", row.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlueScreenWithACodeNobodyKnowsStillShowsTheCode()
    {
        var row = CrashRows.Shutdown(new UnexpectedShutdown(Now, ShutdownCause.BlueScreen, new BugCheckInfo(0x1234, [0, 0, 0, 0])));

        Assert.Equal("Tela azul com o código 0x00001234", row.Title);
        Assert.StartsWith("O Windows não tem uma descrição para este código", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePowerButtonShutdownOfThisPcIsNotPresentedAsAProblem()
    {
        var row = CrashRows.Shutdown(new UnexpectedShutdown(Now, ShutdownCause.PowerButton, null));

        Assert.Equal("Desligamento forçado pelo botão de energia", row.Title);
        Assert.False(row.HasBadge);
        Assert.Equal(MetricState.Neutral, row.Tone);
        Assert.True(row.HasTrailing);
    }

    [Fact]
    public void AShutdownWithNoCauseIsAWarningThatSaysWhatItCouldBe()
    {
        var row = CrashRows.Shutdown(new UnexpectedShutdown(Now, ShutdownCause.Unknown, null));

        Assert.Equal("Sem causa registrada", row.Title);
        Assert.Contains("queda de energia", row.Detail, StringComparison.Ordinal);
        Assert.Equal("Aviso", row.Badge);
        Assert.Equal(MetricState.Warning, row.Tone);
    }

    [Fact]
    public void TheRealDesktopWindowManagerCrashPointsAtTheAmdDriver()
    {
        var group = new AppCrashGroup("dwm.exe", "amdxx64.dll", 0xC0000005, FaultOrigin.GraphicsDriver, "AMD", 3, Now.AddDays(-5), Now);

        var row = CrashRows.App(group);

        Assert.Equal("dwm.exe travou em amdxx64.dll", row.Title);
        Assert.StartsWith("O módulo que falhou é o driver de vídeo AMD.", row.Detail, StringComparison.Ordinal);
        Assert.Contains("Acesso inválido à memória", row.Detail, StringComparison.Ordinal);
        Assert.Equal("Exceção 0xC0000005 · ACCESS_VIOLATION", row.Meta);
        Assert.Equal("Aviso", row.Badge);
        Assert.Equal("3×", row.TrailingTop);
        Assert.Equal("Último: " + Local(Now), row.TrailingBottom);
    }

    [Fact]
    public void AProgramThatCrashesInItsOwnCodeHasASimplerTitleAndNoBadge()
    {
        var group = new AppCrashGroup("jogo.exe", "JOGO.EXE", 0xE0434352, FaultOrigin.Application, null, 1, Now, Now);

        var row = CrashRows.App(group);

        Assert.Equal("jogo.exe travou", row.Title);
        Assert.StartsWith("O erro está no código do próprio programa.", row.Detail, StringComparison.Ordinal);
        Assert.Contains("programa .NET", row.Detail, StringComparison.Ordinal);
        Assert.False(row.HasBadge);
    }

    [Fact]
    public void AnExceptionWithNoNameShowsOnlyItsHexAndKeepsTheOriginText()
    {
        var group = new AppCrashGroup("a.exe", "b.dll", 0x12345678, FaultOrigin.ThirdPartyModule, null, 2, Now, Now);

        var row = CrashRows.App(group);

        Assert.Equal("Exceção 0x12345678", row.Meta);
        Assert.StartsWith("O módulo que falhou é de terceiros", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ADumpFileShowsItsSizeDateAndTheBugCheckItHolds()
    {
        var kernel = new DumpFileInfo(@"C:\WINDOWS\Minidump\a.dmp", "a.dmp", 1_500_000, Now, new BugCheckInfo(0xD1, [1, 2, 3, 4]));
        var user = new DumpFileInfo(@"C:\x\jogo.exe.1.dmp", "jogo.exe.1.dmp", 18_597_719, Now, null);

        var kernelRow = CrashRows.Dump(kernel);
        var userRow = CrashRows.Dump(user);

        Assert.Equal("a.dmp · DRIVER_IRQL_NOT_LESS_OR_EQUAL", kernelRow.Title);
        Assert.Equal("Crítico", kernelRow.Badge);
        Assert.StartsWith("1,43 MB · ", kernelRow.Meta, StringComparison.Ordinal);
        Assert.Equal("jogo.exe.1.dmp", userRow.Title);
        Assert.StartsWith("17,74 MB · ", userRow.Meta, StringComparison.Ordinal);
        Assert.False(userRow.HasBadge);
        Assert.False(userRow.HasDetail);
    }

    [Fact]
    public void TheDumpSettingsOfThisPcAreShownAsFiveRowsAndAdviseTurningThemOn()
    {
        var settings = new DumpSettings(DumpMode.Disabled, @"C:\WINDOWS\Minidump", @"C:\WINDOWS\MEMORY.DMP", false, false);

        var rows = CrashRows.Settings(settings);

        Assert.Equal(["Tipo de dump", "Pasta dos dumps", "Arquivo do dump completo", "Reiniciar sozinho após a tela azul", "Registrar no log de eventos"], rows.Select(r => r.Label));
        Assert.Equal(["Desligado (nada é guardado)", @"C:\WINDOWS\Minidump", @"C:\WINDOWS\MEMORY.DMP", "Não", "Não"], rows.Select(r => r.Value));
        Assert.Contains("Despejo de memória automático", CrashRows.Advice(settings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DumpMode.Automatic, false)]
    [InlineData(DumpMode.Kernel, false)]
    [InlineData(DumpMode.Small, false)]
    [InlineData(DumpMode.Complete, false)]
    [InlineData(DumpMode.Unknown, true)]
    [InlineData(DumpMode.Disabled, true)]
    public void OnlyDumpsThatAreOffOrUndefinedGetAdvice(DumpMode mode, bool advises)
    {
        var settings = new DumpSettings(mode, "a", "b", true, true);

        Assert.Equal(advises, CrashRows.Advice(settings).Length > 0);
    }
}

public sealed class CrashesPageViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    private static CrashReport ThisMachine(IReadOnlyDictionary<string, string>? failures = null) => new(
        TimeSpan.FromDays(30),
        ReadResult.Success(new DumpSettings(DumpMode.Disabled, @"C:\WINDOWS\Minidump", @"C:\WINDOWS\MEMORY.DMP", false, false)),
        [
            new UnexpectedShutdown(Now.AddDays(-3), ShutdownCause.PowerButton, null),
            new UnexpectedShutdown(Now.AddDays(-6), ShutdownCause.Unknown, null),
            new UnexpectedShutdown(Now.AddDays(-9), ShutdownCause.Unknown, null),
        ],
        [
            new AppCrashGroup("dwm.exe", "amdxx64.dll", 0xC0000005, FaultOrigin.GraphicsDriver, "AMD", 1, Now.AddDays(-1), Now.AddDays(-1)),
            new AppCrashGroup("xampp-control.exe", "xampp-control.exe", 0xE0434352, FaultOrigin.Application, null, 2, Now.AddDays(-12), Now.AddDays(-10)),
        ],
        [],
        [new DumpFileInfo(@"C:\Users\x\AppData\Local\CrashDumps\xampp-control.exe.25336.dmp", "xampp-control.exe.25336.dmp", 975_488, Now.AddDays(-10), null)],
        failures ?? new Dictionary<string, string>());

    private static CrashesPageViewModel Create(StubCrashes crashes) => new(crashes, new CollectingLogger<CrashesPageViewModel>());

    [Fact]
    public async Task TheRealPictureOfThisPcFillsEverySection()
    {
        var page = Create(new StubCrashes(_ => Task.FromResult(ThisMachine())));

        await page.RefreshAsync();

        Assert.Equal("0", page.BlueScreensText);
        Assert.Equal("3", page.ShutdownsText);
        Assert.Equal("3", page.AppCrashesText);
        Assert.Equal("2 programa(s) diferentes", page.AppKindsText);
        Assert.Equal(MetricState.Neutral, page.BlueScreensState);
        Assert.Equal(MetricState.Warning, page.ShutdownsState);
        Assert.Equal(3, page.Shutdowns.Count);
        Assert.Equal(2, page.AppCrashes.Count);
        Assert.Empty(page.KernelDumps);
        Assert.Single(page.UserDumps);
        Assert.True(page.ShowKernelDumpsEmpty);
        Assert.False(page.ShowShutdownsEmpty);
        Assert.False(page.ShowUserDumpsEmpty);
        Assert.False(page.IsLoading);
    }

    [Fact]
    public async Task DumpsThatAreOffShowAWarningStatusAndTheAdvice()
    {
        var page = Create(new StubCrashes(_ => Task.FromResult(ThisMachine())));

        await page.RefreshAsync();

        Assert.True(page.Settings.IsReady);
        Assert.Equal("Dumps desligados", page.Settings.StatusText);
        Assert.Equal(MetricState.Warning, page.Settings.StatusTone);
        Assert.Equal(5, page.Settings.Rows.Count);
        Assert.True(page.HasAdvice);
        Assert.Contains("não guardar nada", page.Advice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DumpsThatAreOnShowAGoodStatusAndNoAdvice()
    {
        var report = ThisMachine() with { Settings = ReadResult.Success(new DumpSettings(DumpMode.Automatic, "a", "b", true, true)) };
        var page = Create(new StubCrashes(_ => Task.FromResult(report)));

        await page.RefreshAsync();

        Assert.Equal("Dumps ligados", page.Settings.StatusText);
        Assert.Equal(MetricState.Good, page.Settings.StatusTone);
        Assert.False(page.HasAdvice);
    }

    [Fact]
    public async Task ABlueScreenTurnsTheCounterCriticalAndListsItsCode()
    {
        var blue = new UnexpectedShutdown(Now, ShutdownCause.BlueScreen, new BugCheckInfo(0x116, [1, 2, 3, 4]));
        var report = ThisMachine() with { Shutdowns = [blue] };
        var page = Create(new StubCrashes(_ => Task.FromResult(report)));

        await page.RefreshAsync();

        Assert.Equal("1", page.BlueScreensText);
        Assert.Equal(MetricState.Critical, page.BlueScreensState);
        Assert.Equal("Tela azul: VIDEO_TDR_FAILURE", Assert.Single(page.Shutdowns).Title);
    }

    [Fact]
    public async Task APartThatFailedIsReportedInOneBannerInAFixedOrder()
    {
        var failures = new Dictionary<string, string>
        {
            [CrashService.KernelDumpsPart] = "Sem permissão para ler a pasta. Abra o m1vClock como administrador.",
            [CrashService.ShutdownsPart] = "Sem permissão para ler o log System.",
        };
        var page = Create(new StubCrashes(_ => Task.FromResult(ThisMachine(failures))));

        await page.RefreshAsync();

        Assert.True(page.HasProblems);
        var lines = page.Problems.Split(Environment.NewLine);
        Assert.Equal("Não foi possível ler os reinícios inesperados: Sem permissão para ler o log System.", lines[0]);
        Assert.Equal("Não foi possível ler os dumps de tela azul: Sem permissão para ler a pasta. Abra o m1vClock como administrador.", lines[1]);
    }

    [Fact]
    public async Task SettingsThatCouldNotBeReadAreShownAsAFailedCardWithoutAdvice()
    {
        var report = ThisMachine() with { Settings = ReadResult.Failure<DumpSettings>("registro bloqueado") };
        var page = Create(new StubCrashes(_ => Task.FromResult(report)));

        await page.RefreshAsync();

        Assert.True(page.Settings.IsFailed);
        Assert.False(page.HasAdvice);
    }

    [Fact]
    public async Task AQuietPcShowsEveryEmptyMessage()
    {
        var empty = new CrashReport(TimeSpan.FromDays(30), ReadResult.Success(new DumpSettings(DumpMode.Automatic, "a", "b", true, true)), [], [], [], [], new Dictionary<string, string>());
        var page = Create(new StubCrashes(_ => Task.FromResult(empty)));

        await page.RefreshAsync();

        Assert.True(page.ShowShutdownsEmpty && page.ShowAppsEmpty && page.ShowKernelDumpsEmpty && page.ShowUserDumpsEmpty);
        Assert.Equal("0", page.AppCrashesText);
    }

    [Fact]
    public async Task AReadThatBlowsUpShowsThePageErrorAndStopsLoading()
    {
        var page = Create(new StubCrashes(_ => throw new InvalidOperationException("quebrou")));

        await page.RefreshAsync();

        Assert.True(page.HasPageError);
        Assert.False(page.IsLoading);
        Assert.Empty(page.Shutdowns);
    }

    [Fact]
    public async Task EachPeriodAsksForItsOwnNumberOfDays()
    {
        var stub = new StubCrashes(_ => Task.FromResult(ThisMachine()));
        var page = Create(stub);

        await page.RefreshAsync();
        page.WindowIndex = 2;
        await stub.WaitForReadsAsync(2);
        page.WindowIndex = 0;
        await stub.WaitForReadsAsync(3);

        Assert.Equal([TimeSpan.FromDays(30), TimeSpan.FromDays(90), TimeSpan.FromDays(7)], stub.Windows);
        Assert.Equal(["7 dias", "30 dias", "90 dias"], page.WindowLabels);
    }
}

public sealed class CrashTextsTests
{
    private static readonly ResourceManager Resources = new("M1vClock.App.Localization.Strings", typeof(Loc).Assembly);

    public static TheoryData<string> Languages => ["pt-BR", "en-US"];

    private static bool Missing(string key, string language)
    {
        var text = Resources.GetString(key, CultureInfo.GetCultureInfo(language));
        return string.IsNullOrWhiteSpace(text);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryBlueScreenNameAndEveryExceptionNameHasAnExplanation(string language)
    {
        var missing = BugCheckCatalog.AllNames.Select(n => $"Diag.BugCheck.{n}.Detail")
            .Concat(ExceptionCatalog.AllNames.Select(n => $"Diag.Exception.{n}.Detail"))
            .Where(key => Missing(key, language))
            .ToList();

        Assert.True(missing.Count == 0, "faltam: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryModeOriginAndPartOfTheReportHasItsText(string language)
    {
        var keys = Enum.GetValues<DumpMode>().Select(m => $"Diag.Dump.Mode.{m}")
            .Concat(Enum.GetValues<FaultOrigin>().Select(o => $"Diag.Crashes.Origin.{o}"))
            .Concat(
            [
                $"Diag.Crashes.Part.{CrashService.SettingsPart}",
                $"Diag.Crashes.Part.{CrashService.ShutdownsPart}",
                $"Diag.Crashes.Part.{CrashService.AppCrashesPart}",
                $"Diag.Crashes.Part.{CrashService.KernelDumpsPart}",
                $"Diag.Crashes.Part.{CrashService.UserDumpsPart}",
            ]);

        var missing = keys.Where(key => Missing(key, language)).ToList();

        Assert.True(missing.Count == 0, "faltam: " + string.Join(", ", missing));
    }
}

internal sealed class StubCrashes(Func<CancellationToken, Task<CrashReport>> read) : ICrashService
{
    private readonly object _gate = new();
    private readonly List<TimeSpan> _windows = [];

    public IReadOnlyList<TimeSpan> Windows
    {
        get
        {
            lock (_gate)
            {
                return [.. _windows];
            }
        }
    }

    public Task<CrashReport> ReadAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _windows.Add(window);
        }

        return read(cancellationToken);
    }

    public async Task WaitForReadsAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Windows.Count < count)
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
