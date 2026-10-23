using System.Globalization;
using System.Resources;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Diagnostics;
using M1vClock.Diagnostics.Events;

namespace M1vClock.App.Tests;

public sealed class EventsPageViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.FromHours(-3));

    private static IEnumerable<RawEvent> Repeat(string log, string provider, int id, EventSeverity severity, int count, string? message = null) =>
        Enumerable.Range(0, count).Select(i => new RawEvent(log, provider, id, severity, Now.AddMinutes(-i), i == 0 ? message : null));

    private static EventReport ThisMachine(bool truncated = false, IReadOnlyDictionary<string, string>? failed = null)
    {
        var events = new List<RawEvent>();
        events.AddRange(Repeat("Application", "ESENT", 490, EventSeverity.Error, 15406));
        events.AddRange(Repeat("Application", "ESENT", 455, EventSeverity.Error, 15406));
        events.AddRange(Repeat("System", "Microsoft-Windows-DistributedCOM", 10016, EventSeverity.Error, 90));
        events.AddRange(Repeat("System", "winsrvext", 100, EventSeverity.Warning, 61, "O serviço de teste falhou."));
        events.AddRange(Repeat("System", "Microsoft-Windows-Time-Service", 135, EventSeverity.Warning, 36));
        events.AddRange(Repeat("System", "Microsoft-Windows-Kernel-Power", 41, EventSeverity.Critical, 4, "O sistema foi reiniciado sem ser desligado corretamente."));
        return new EventReport(TimeSpan.FromDays(7), EventService.Group(events), failed ?? new Dictionary<string, string>(), truncated);
    }

    private static EventsPageViewModel Create(StubEvents events) => new(events, new CollectingLogger<EventsPageViewModel>());

    [Fact]
    public async Task TheNoisyAndHarmlessEventsAreHiddenByDefaultAndCounted()
    {
        var page = Create(new StubEvents(_ => Task.FromResult(ThisMachine())));

        await page.RefreshAsync();

        Assert.Equal(["Kernel-Power", "winsrvext", "Time-Service"], page.Items.Select(i => i.Group.Provider.Replace("Microsoft-Windows-", string.Empty, StringComparison.Ordinal)));
        Assert.True(page.HasHidden);
        Assert.Equal("30.902 eventos repetitivos escondidos, de 3 tipo(s). Ligue “Mostrar ruído repetido” para ver.", page.HiddenText);
        Assert.False(page.IsEmpty);
    }

    [Fact]
    public async Task ShowingTheNoiseListsEverythingAndDropsTheHint()
    {
        var page = Create(new StubEvents(_ => Task.FromResult(ThisMachine())));
        await page.RefreshAsync();

        page.ShowNoise = true;

        Assert.Equal(6, page.Items.Count);
        Assert.Equal("ESENT", page.Items[^1].Group.Provider);
        Assert.False(page.HasHidden);
        page.ShowNoise = false;
        Assert.Equal(3, page.Items.Count);
        Assert.True(page.HasHidden);
    }

    [Fact]
    public async Task TheSummaryCountsEveryEventBySeverity()
    {
        var page = Create(new StubEvents(_ => Task.FromResult(ThisMachine())));

        await page.RefreshAsync();

        Assert.Equal("4", page.CriticalText);
        Assert.Equal("30.902", page.ErrorText);
        Assert.Equal("97", page.WarningText);
        Assert.Equal("6 tipo(s) diferentes", page.KindsText);
        Assert.Equal(MetricState.Critical, page.CriticalState);
        Assert.Equal(MetricState.Warning, page.ErrorState);
    }

    [Fact]
    public async Task AKnownEventShowsItsExplanationAndAnUnknownOneShowsTheWindowsMessage()
    {
        var page = Create(new StubEvents(_ => Task.FromResult(ThisMachine())));
        await page.RefreshAsync();

        var known = page.Items[0];
        var unknown = page.Items.Single(i => i.Group.Provider == "winsrvext");

        Assert.Equal("O PC desligou sem avisar", known.Title);
        Assert.StartsWith("O Windows reiniciou sem um desligamento limpo", known.Detail, StringComparison.Ordinal);
        Assert.Equal("O sistema foi reiniciado sem ser desligado corretamente.", known.Message);
        Assert.Equal("Desligamento", known.KindText);
        Assert.Equal("Crítico", known.SeverityText);
        Assert.Equal(MetricState.Critical, known.Tone);
        Assert.Equal("Sistema", known.LogText);
        Assert.Equal("Microsoft-Windows-Kernel-Power · ID 41", known.SourceText);
        Assert.Equal("4×", known.CountText);

        Assert.Equal("winsrvext", unknown.Title);
        Assert.Equal("O serviço de teste falhou.", unknown.Detail);
        Assert.False(unknown.HasMessage);
        Assert.False(unknown.HasKind);
        Assert.Equal("Aviso", unknown.SeverityText);
        Assert.Equal(MetricState.Neutral, unknown.Tone);
    }

    [Fact]
    public async Task TheLastTimeIsShownInLocalTime()
    {
        var page = Create(new StubEvents(_ => Task.FromResult(ThisMachine())));
        await page.RefreshAsync();

        var expected = "Último: " + Now.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("pt-BR"));

        Assert.Equal(expected, page.Items[0].LastText);
        Assert.Contains("Crítico: O PC desligou sem avisar. 4×.", page.Items[0].AccessibleName, StringComparison.Ordinal);
        Assert.Equal(page.Items[0].AccessibleName, page.Items[0].ToString());
    }

    [Fact]
    public async Task AFailedLogAndATruncatedReadAreSaidInOneBanner()
    {
        var report = ThisMachine(truncated: true, failed: new Dictionary<string, string> { ["Application"] = "Sem permissão para ler o log Application." });
        var page = Create(new StubEvents(_ => Task.FromResult(report)));

        await page.RefreshAsync();

        Assert.True(page.HasProblems);
        Assert.Contains("Não foi possível ler o log Application: Sem permissão para ler o log Application.", page.Problems, StringComparison.Ordinal);
        Assert.Contains("O período tem eventos demais", page.Problems, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingToReportShowsTheEmptyMessage()
    {
        var empty = new EventReport(TimeSpan.FromDays(7), [], new Dictionary<string, string>(), false);
        var page = Create(new StubEvents(_ => Task.FromResult(empty)));

        await page.RefreshAsync();

        Assert.True(page.IsEmpty);
        Assert.Empty(page.Items);
        Assert.False(page.HasHidden);
        Assert.Equal("0", page.CriticalText);
    }

    [Fact]
    public async Task AReadThatBlowsUpShowsThePageErrorAndStopsLoading()
    {
        var page = Create(new StubEvents(_ => throw new InvalidOperationException("quebrou")));

        await page.RefreshAsync();

        Assert.True(page.HasPageError);
        Assert.False(page.IsLoading);
        Assert.Empty(page.Items);
        Assert.False(page.IsEmpty);
    }

    [Fact]
    public async Task EachPeriodAsksForItsOwnNumberOfDays()
    {
        var stub = new StubEvents(_ => Task.FromResult(ThisMachine()));
        var page = Create(stub);

        await page.RefreshAsync();
        page.WindowIndex = 3;
        await stub.WaitForReadsAsync(2);
        page.WindowIndex = 0;
        await stub.WaitForReadsAsync(3);

        Assert.Equal([TimeSpan.FromDays(7), TimeSpan.FromDays(30), TimeSpan.FromDays(1)], stub.Windows);
        Assert.Equal(["24 horas", "7 dias", "14 dias", "30 dias"], page.WindowLabels);
    }

    [Fact]
    public void OnlyTheRepeatedOrHarmlessEventsCountAsNoise()
    {
        var group = EventService.Group(Repeat("System", "disk", 153, EventSeverity.Warning, 2)).Single();
        var dcom = EventService.Group(Repeat("System", "Microsoft-Windows-DistributedCOM", 10016, EventSeverity.Error, 2)).Single();
        var loud = EventService.Group(Repeat("System", "winsrvext", 100, EventSeverity.Warning, EventGroup.NoisyThreshold)).Single();

        Assert.False(EventsPageViewModel.IsNoise(group));
        Assert.True(EventsPageViewModel.IsNoise(dcom));
        Assert.True(EventsPageViewModel.IsNoise(loud));
    }
}

public sealed class EventTextsTests
{
    private static readonly ResourceManager Resources = new("M1vClock.App.Localization.Strings", typeof(Loc).Assembly);

    public static TheoryData<string> Languages => ["pt-BR", "en-US"];

    private static bool Missing(string text) => text.Length > 0 && text[0] == '[';

    private static string Text(string key, string language) => Resources.GetString(key, CultureInfo.GetCultureInfo(language)) ?? $"[{key}]";

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryKnownEventHasATitleAndAnExplanation(string language)
    {
        foreach (var key in EventKnowledge.AllKeys)
        {
            var title = Text($"Diag.Known.{key}.Title", language);
            var detail = Text($"Diag.Known.{key}.Detail", language);
            Assert.False(Missing(title), $"falta o título de {key}");
            Assert.False(Missing(detail), $"falta a explicação de {key}");
            Assert.NotEqual(title, detail);
        }

        foreach (var kind in Enum.GetValues<KnownEventKind>())
        {
            Assert.False(Missing(Text($"Diag.Kind.{kind}", language)), $"falta o tipo {kind}");
        }

        foreach (var severity in Enum.GetValues<EventSeverity>())
        {
            Assert.False(Missing(Text($"Diag.Severity.{severity}", language)), $"falta a gravidade {severity}");
        }

        foreach (var log in EventService.Logs)
        {
            Assert.False(Missing(Text($"Diag.Events.Log.{log}", language)), $"falta o nome do log {log}");
        }
    }

    [Fact]
    public void EveryKnownEventTitleIsTranslatedToEnglishDifferentlyFromPortuguese()
    {
        foreach (var key in EventKnowledge.AllKeys)
        {
            Assert.NotEqual(Text($"Diag.Known.{key}.Title", "pt-BR"), Text($"Diag.Known.{key}.Title", "en-US"));
        }
    }
}

internal sealed class StubEvents(Func<CancellationToken, Task<EventReport>> read) : IEventService
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

    public Task<EventReport> ReadAsync(TimeSpan window, CancellationToken cancellationToken)
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
