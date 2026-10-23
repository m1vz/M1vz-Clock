using System.Globalization;
using System.Resources;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Diagnostics;
using M1vClock.Core.Results;
using M1vClock.Diagnostics.Reliability;

namespace M1vClock.App.Tests;

public sealed class ReliabilityRowsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheRealApplicationErrorOfThisPcIsAFailureRowWithItsProgramAndSource()
    {
        var row = ReliabilityRows.Record(new ReliabilityRecord(Now, "Application Error", 1000, "dwm.exe", ReliabilityKind.AppCrash));

        Assert.Equal("Programa travou", row.Title);
        Assert.Equal("dwm.exe", row.Detail);
        Assert.Equal("Application Error · ID 1000", row.Meta);
        Assert.Equal("Erro", row.Badge);
        Assert.Equal(MetricState.Warning, row.Tone);
        Assert.Equal(CrashRows.Time(Now), row.TrailingTop);
    }

    [Fact]
    public void AnInstallRowHasNoBadgeAndStaysNeutral()
    {
        var row = ReliabilityRows.Record(new ReliabilityRecord(Now, "MsiInstaller", 1033, "AMD_Chipset_Drivers", ReliabilityKind.SoftwareInstalled));

        Assert.Equal("Programa instalado", row.Title);
        Assert.Equal("AMD_Chipset_Drivers", row.Detail);
        Assert.False(row.HasBadge);
        Assert.Equal(MetricState.Neutral, row.Tone);
    }

    [Theory]
    [InlineData(StabilityVerdict.Stable, "Estável", MetricState.Good)]
    [InlineData(StabilityVerdict.Occasional, "Falhas ocasionais", MetricState.Warning)]
    [InlineData(StabilityVerdict.Unstable, "Instável", MetricState.Critical)]
    [InlineData(StabilityVerdict.Unknown, "Sem dados", MetricState.Neutral)]
    public void EachVerdictHasATextAndATone(StabilityVerdict verdict, string text, MetricState tone)
    {
        Assert.Equal(text, ReliabilityRows.Verdict(verdict));
        Assert.Equal(tone, ReliabilityRows.Tone(verdict));
    }

    [Fact]
    public void TheIndexIsShownWithItsDecimalsInPortuguese()
    {
        Assert.Equal("7,05", ReliabilityRows.Index(7.05));
        Assert.Equal("10,0", ReliabilityRows.Index(10));
        Assert.Equal("5,84", ReliabilityRows.Index(5.8416));
        Assert.Equal("—", ReliabilityRows.Index(null));
    }

    [Fact]
    public void TheChartTakesDaysAsTimeAndOnlyFailuresBecomeMarkers()
    {
        var points = ReliabilityRows.ChartPoints([new StabilityPoint(DateTimeOffset.UnixEpoch.AddDays(2), 7), new StabilityPoint(DateTimeOffset.UnixEpoch.AddDays(3), 6.5)]);
        var markers = ReliabilityRows.FailureMarkers(
        [
            new ReliabilityRecord(DateTimeOffset.UnixEpoch.AddDays(3), "EventLog", 6008, "Windows", ReliabilityKind.UnexpectedShutdown),
            new ReliabilityRecord(DateTimeOffset.UnixEpoch.AddDays(1), "Application Error", 1000, "x", ReliabilityKind.AppCrash),
            new ReliabilityRecord(DateTimeOffset.UnixEpoch.AddDays(2), "MsiInstaller", 1033, "y", ReliabilityKind.SoftwareInstalled),
        ]);

        Assert.Equal([new ChartPoint(2, 7), new ChartPoint(3, 6.5)], points);
        Assert.Equal([1.0, 3.0], markers);
    }

    [Fact]
    public void TheChartNameSaysTheCurrentIndexForScreenReaders()
    {
        Assert.Equal("Índice de estabilidade ao longo do período; agora 7,05 de 10", ReliabilityRows.ChartName(7.05));
    }
}

public sealed class ReliabilityPageViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    private static ReliabilityReport ThisMachine() => new(
        TimeSpan.FromDays(30),
        ReadResult.Success<IReadOnlyList<StabilityPoint>>([new StabilityPoint(Now.AddHours(-2), 5.842), new StabilityPoint(Now.AddHours(-1), 7.032), new StabilityPoint(Now, 7.05)]),
        ReadResult.Success<IReadOnlyList<ReliabilityRecord>>(
        [
            new ReliabilityRecord(Now.AddDays(-1), "Application Error", 1000, "dwm.exe", ReliabilityKind.AppCrash),
            new ReliabilityRecord(Now.AddDays(-3), "EventLog", 6008, "Windows", ReliabilityKind.UnexpectedShutdown),
            new ReliabilityRecord(Now.AddDays(-4), "MsiInstaller", 1033, "AMD_Chipset_Drivers", ReliabilityKind.SoftwareInstalled),
            new ReliabilityRecord(Now.AddDays(-5), "Microsoft-Windows-WindowsUpdateClient", 19, "KB5127648", ReliabilityKind.UpdateInstalled),
            new ReliabilityRecord(Now.AddDays(-6), "MsiInstaller", 1038, "Algo", ReliabilityKind.Other),
        ]));

    private static ReliabilityPageViewModel Create(StubReliability service) => new(service, new CollectingLogger<ReliabilityPageViewModel>());

    [Fact]
    public async Task TheNumbersOfThisPcFillTheCardsAndTheChart()
    {
        var page = Create(new StubReliability(_ => Task.FromResult(ThisMachine())));

        await page.RefreshAsync();

        Assert.Equal("7,05", page.CurrentText);
        Assert.Equal("5,84", page.LowestText);
        Assert.Equal("Falhas ocasionais", page.VerdictText);
        Assert.Equal(MetricState.Warning, page.VerdictTone);
        Assert.Equal("2", page.FailuresText);
        Assert.Equal("2", page.ChangesText);
        Assert.True(page.HasChart);
        Assert.Equal(3, page.ChartPoints?.Count);
        Assert.Equal(2, page.Markers?.Count);
        Assert.Contains("agora 7,05 de 10", page.ChartName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyFailuresAreListedByDefaultAndTheRestIsCountedAsHidden()
    {
        var page = Create(new StubReliability(_ => Task.FromResult(ThisMachine())));

        await page.RefreshAsync();

        Assert.Equal(["Programa travou", "Desligamento inesperado"], page.Items.Select(i => i.Title));
        Assert.True(page.HasHidden);
        Assert.StartsWith("3 registros que não são falhas", page.HiddenText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurningOnTheSwitchListsEveryRecordNewestFirstAndDropsTheHint()
    {
        var page = Create(new StubReliability(_ => Task.FromResult(ThisMachine())));
        await page.RefreshAsync();

        page.ShowChanges = true;

        Assert.Equal(5, page.Items.Count);
        Assert.False(page.HasHidden);
        page.ShowChanges = false;
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasHidden);
    }

    [Fact]
    public async Task AFailedStabilityReadStillShowsTheRecordsAndSaysWhatFailed()
    {
        var report = ThisMachine() with { Stability = ReadResult.Failure<IReadOnlyList<StabilityPoint>>("WMI quebrado") };
        var page = Create(new StubReliability(_ => Task.FromResult(report)));

        await page.RefreshAsync();

        Assert.False(page.HasChart);
        Assert.Equal("—", page.CurrentText);
        Assert.Equal("Sem dados", page.VerdictText);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Não foi possível ler o índice de estabilidade: WMI quebrado", page.Problems);
    }

    [Fact]
    public async Task AFailedRecordsReadShowsDashesInsteadOfZeros()
    {
        var report = ThisMachine() with { Records = ReadResult.Failure<IReadOnlyList<ReliabilityRecord>>("sem acesso") };
        var page = Create(new StubReliability(_ => Task.FromResult(report)));

        await page.RefreshAsync();

        Assert.Equal("—", page.FailuresText);
        Assert.Equal("—", page.ChangesText);
        Assert.Empty(page.Items);
        Assert.Contains("os registros", page.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoFailuresAtAllShowsTheEmptyMessage()
    {
        var report = ThisMachine() with { Records = ReadResult.Success<IReadOnlyList<ReliabilityRecord>>([]) };
        var page = Create(new StubReliability(_ => Task.FromResult(report)));

        await page.RefreshAsync();

        Assert.True(page.IsEmpty);
        Assert.Equal("0", page.FailuresText);
    }

    [Fact]
    public async Task AReadThatBlowsUpShowsThePageErrorAndStopsLoading()
    {
        var page = Create(new StubReliability(_ => throw new InvalidOperationException("quebrou")));

        await page.RefreshAsync();

        Assert.True(page.HasPageError);
        Assert.False(page.IsLoading);
    }

    [Fact]
    public async Task EachPeriodAsksForItsOwnNumberOfDays()
    {
        var stub = new StubReliability(_ => Task.FromResult(ThisMachine()));
        var page = Create(stub);

        await page.RefreshAsync();
        page.WindowIndex = 2;
        await stub.WaitForReadsAsync(2);

        Assert.Equal([TimeSpan.FromDays(30), TimeSpan.FromDays(90)], stub.Windows);
    }

    [Fact]
    public void TheGuidesMarkTheLinesThatSeparateTheVerdicts()
    {
        Assert.Equal([1.0, ReliabilityReport.OccasionalFrom, ReliabilityReport.StableFrom, 10.0], ReliabilityPageViewModel.Guides);
    }
}

public sealed class ReliabilityTextsTests
{
    private static readonly ResourceManager Resources = new("M1vClock.App.Localization.Strings", typeof(Loc).Assembly);

    public static TheoryData<string> Languages => ["pt-BR", "en-US"];

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryKindAndEveryVerdictHasItsText(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var keys = Enum.GetValues<ReliabilityKind>().Select(k => $"Diag.Reliability.Kind.{k}")
            .Concat(Enum.GetValues<StabilityVerdict>().Select(v => $"Diag.Reliability.Verdict.{v}"));

        var missing = keys.Where(k => string.IsNullOrWhiteSpace(Resources.GetString(k, culture))).ToList();

        Assert.True(missing.Count == 0, "faltam: " + string.Join(", ", missing));
    }
}

internal sealed class StubReliability(Func<CancellationToken, Task<ReliabilityReport>> read) : IReliabilityService
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

    public Task<ReliabilityReport> ReadAsync(TimeSpan window, CancellationToken cancellationToken)
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
