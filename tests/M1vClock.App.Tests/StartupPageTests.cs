using System.Globalization;
using System.Resources;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Dialogs;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Optimize;
using M1vClock.Core.Journal;
using M1vClock.Core.RestorePoints;
using M1vClock.Core.Results;
using M1vClock.Optimize.Startup;

namespace M1vClock.App.Tests;

public sealed class StartupRowsTests
{
    private static StartupEntry Entry(string name, StartupLocation location, bool enabled, bool missing = false, string? description = null, string? company = null) =>
        new(name, location, "\"C:\\x\\" + name + ".exe\"", "C:\\x\\" + name + ".exe", enabled, missing, description, company);

    [Fact]
    public void AnEnabledUserEntryShowsItsCompanyItsPlaceAndAnOffButton()
    {
        var row = new StartupRowViewModel(Entry("RiotClient", StartupLocation.UserRun, true, description: "Riot Client", company: "Riot Games, Inc."), isElevated: false);

        Assert.Equal("Riot Client", row.Title);
        Assert.Equal("Riot Games, Inc. · RiotClient", row.Detail);
        Assert.Equal("Só para o seu usuário (registro) · C:\\x\\RiotClient.exe", row.Meta);
        Assert.Equal("Abre sozinho", row.Badge);
        Assert.Equal(MetricState.Neutral, row.Tone);
        Assert.Equal("Desligar", row.ActionText);
        Assert.True(row.CanChange);
        Assert.Equal("Desligar", row.ActionToolTip);
    }

    [Fact]
    public void ADisabledEntryIsGoodNewsAndOffersToTurnItBackOn()
    {
        var row = new StartupRowViewModel(Entry("Steam", StartupLocation.UserRun, false), isElevated: false);

        Assert.Equal("Desligado", row.Badge);
        Assert.Equal(MetricState.Good, row.Tone);
        Assert.Equal("Ligar", row.ActionText);
    }

    [Fact]
    public void AnEntryWhoseFileIsGoneIsAWarningWhateverItsFlagSays()
    {
        var row = new StartupRowViewModel(Entry("AMDNoiseSuppression", StartupLocation.UserRun, false, missing: true), isElevated: false);

        Assert.Equal("Arquivo ausente", row.Badge);
        Assert.Equal(MetricState.Warning, row.Tone);
    }

    [Fact]
    public void AMachineEntryNeedsAdministratorAndSaysSoInTheToolTip()
    {
        var entry = Entry("Riot Vanguard", StartupLocation.MachineRun, true);

        var user = new StartupRowViewModel(entry, isElevated: false);
        var admin = new StartupRowViewModel(entry, isElevated: true);

        Assert.False(user.CanChange);
        Assert.Equal("Precisa do m1vClock como administrador para mudar este programa.", user.ActionToolTip);
        Assert.True(admin.CanChange);
        Assert.Equal("Todos os usuários (registro) · C:\\x\\Riot Vanguard.exe", admin.Meta);
    }

    [Fact]
    public void WithoutACompanyTheDetailIsJustTheEntryName()
    {
        Assert.Equal("Medal", new StartupRowViewModel(Entry("Medal", StartupLocation.UserRun, true), false).Detail);
    }

    [Fact]
    public void TheAccessibleNameSaysWhatItIsAndItsState()
    {
        var row = new StartupRowViewModel(Entry("Medal", StartupLocation.UserRun, true, description: "Medal"), false);

        Assert.Equal("Medal, Abre sozinho", row.AccessibleName);
        Assert.Equal(row.AccessibleName, row.ToString());
    }
}

public sealed class StartupPageViewModelTests : IDisposable
{
    private readonly DialogService _dialogs = new();
    private readonly List<DialogRequest> _shown = [];
    private bool _answer = true;

    public StartupPageViewModelTests()
    {
        _dialogs.Opened += (_, _) =>
        {
            _shown.Add(_dialogs.Current!);
            if (_answer)
            {
                _dialogs.ConfirmCommand.Execute(null);
            }
            else
            {
                _dialogs.CancelCommand.Execute(null);
            }
        };
    }

    public void Dispose() => GC.SuppressFinalize(this);

    private sealed class StubStartup : IStartupService
    {
        public ReadResult<StartupReport> Report { get; set; } = ReadResult.Success(new StartupReport([]));

        public List<(string Name, bool Enabled)> Changes { get; } = [];

        public Func<StartupEntry, bool, ApplyOutcome>? Apply { get; set; }

        public Task<ReadResult<StartupReport>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Report);

        public Task<ApplyOutcome> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken cancellationToken)
        {
            Changes.Add((entry.Name, enabled));
            return Task.FromResult(Apply!(entry, enabled));
        }
    }

    private static StartupEntry Entry(string name, StartupLocation location, bool enabled, bool missing = false) =>
        new(name, location, "x", "x", enabled, missing, name, null);

    private static ApplyOutcome Outcome(ChangeStatus status, string? error = null, RestorePointGateStatus gate = RestorePointGateStatus.NotRequired) =>
        new(
            new JournalEntry
            {
                Id = Guid.NewGuid(),
                Sequence = 1,
                CreatedAt = DateTimeOffset.UnixEpoch,
                Kind = "registry-value",
                Module = "Optimize",
                Title = "t",
                Target = "t",
                TargetDisplay = "t",
                Address = [],
                Before = [],
                Desired = [],
                Status = status,
                Error = error,
            },
            new RestorePointGateResult(gate, gate == RestorePointGateStatus.Failed ? "sem proteção" : null));

    private StartupPageViewModel Create(StubStartup startup, bool elevated = false) =>
        new(startup, _dialogs, new AppInfo("0.1.0", @"C:\dados", elevated), new CollectingLogger<StartupPageViewModel>());

    private static StubStartup RealisticStartup() => new()
    {
        Report = ReadResult.Success(new StartupReport(
        [
            Entry("Medal", StartupLocation.UserRun, true),
            Entry("Riot Vanguard", StartupLocation.MachineRun, true),
            Entry("Steam", StartupLocation.UserRun, false),
            Entry("AMDNoiseSuppression", StartupLocation.UserRun, false, missing: true),
        ])),
        Apply = (_, _) => Outcome(ChangeStatus.Applied),
    };

    [Fact]
    public async Task TheSummaryCountsEveryEntryByState()
    {
        var page = Create(RealisticStartup());

        await page.RefreshAsync();

        Assert.Equal("4", page.TotalText);
        Assert.Equal("2", page.EnabledText);
        Assert.Equal("2", page.DisabledText);
        Assert.Equal("1", page.MissingText);
        Assert.Equal(MetricState.Warning, page.MissingState);
        Assert.Equal(4, page.Items.Count);
        Assert.False(page.IsLoading);
        Assert.False(page.IsEmpty);
    }

    [Fact]
    public async Task TurningOffAsksFirstSpellingOutWhatChangesAndHowToUndo()
    {
        var startup = RealisticStartup();
        var page = Create(startup);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(i => i.Title == "Medal"));

        var request = Assert.Single(_shown);
        Assert.Equal("Desligar da inicialização?", request.Title);
        Assert.Contains("Medal deixa de abrir sozinho", request.Message, StringComparison.Ordinal);
        Assert.Equal(["O estado de Medal na inicialização passa a desligado."], request.Changes);
        Assert.Contains("Desfazer na página Histórico", request.Undo, StringComparison.Ordinal);
        Assert.Equal("Desligar", request.ConfirmText);
        Assert.Equal([("Medal", false)], startup.Changes);
    }

    [Fact]
    public async Task TurningOnUsesItsOwnWordsAndWritesTheEnabledState()
    {
        var startup = RealisticStartup();
        var page = Create(startup);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(i => i.Title == "Steam"));

        Assert.Equal("Ligar na inicialização?", Assert.Single(_shown).Title);
        Assert.Equal([("Steam", true)], startup.Changes);
        Assert.Equal("Steam foi ligado na inicialização. Você pode desfazer na página Histórico.", page.Notice);
    }

    [Fact]
    public async Task RefusingTheQuestionChangesNothing()
    {
        _answer = false;
        var startup = RealisticStartup();
        var page = Create(startup);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items[0]);

        Assert.Single(_shown);
        Assert.Empty(startup.Changes);
        Assert.False(page.HasNotice);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task AMachineEntryIsNotTouchedWithoutAdministratorAndNoQuestionIsAsked()
    {
        var startup = RealisticStartup();
        var page = Create(startup, elevated: false);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(i => i.Title == "Riot Vanguard"));

        Assert.Empty(_shown);
        Assert.Empty(startup.Changes);
    }

    [Fact]
    public async Task AMachineEntryCanBeChangedWhenElevated()
    {
        var startup = RealisticStartup();
        var page = Create(startup, elevated: true);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(i => i.Title == "Riot Vanguard"));

        Assert.Equal([("Riot Vanguard", false)], startup.Changes);
    }

    [Fact]
    public async Task AChangeThatFailedShowsTheReasonAndNoSuccessMessage()
    {
        var startup = RealisticStartup();
        startup.Apply = (_, _) => Outcome(ChangeStatus.ApplyFailed, "Acesso negado.");
        var page = Create(startup);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(i => i.Title == "Medal"));

        Assert.Equal("Não foi possível mudar Medal: Acesso negado.", page.Problem);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task ARestorePointThatCouldNotBeMadeIsMentionedButTheChangeStillCounts()
    {
        var startup = RealisticStartup();
        startup.Apply = (_, _) => Outcome(ChangeStatus.Applied, gate: RestorePointGateStatus.Failed);
        var page = Create(startup, elevated: true);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(i => i.Title == "Riot Vanguard"));

        Assert.StartsWith("Riot Vanguard foi desligado da inicialização.", page.Notice, StringComparison.Ordinal);
        Assert.Contains("não foi possível criar o ponto de restauração antes da mudança (sem proteção)", page.Notice, StringComparison.Ordinal);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task AfterAChangeTheListIsReadAgain()
    {
        var startup = RealisticStartup();
        var reads = 0;
        var page = Create(startup);
        await page.RefreshAsync();
        startup.Apply = (_, _) =>
        {
            reads++;
            startup.Report = ReadResult.Success(new StartupReport([Entry("Medal", StartupLocation.UserRun, false)]));
            return Outcome(ChangeStatus.Applied);
        };

        await page.ChangeAsync(page.Items.Single(i => i.Title == "Medal"));

        Assert.Equal(1, reads);
        Assert.Single(page.Items);
        Assert.Equal("Desligado", page.Items[0].Badge);
        Assert.True(page.HasNotice);
    }

    [Fact]
    public async Task ANullRowDoesNothing()
    {
        var page = Create(RealisticStartup());

        await page.ChangeAsync(null);

        Assert.Empty(_shown);
    }

    [Fact]
    public async Task ASourceThatCannotBeReadShowsTheProblemAndNoRows()
    {
        var startup = new StubStartup { Report = ReadResult.Failure<StartupReport>("sem acesso") };
        var page = Create(startup);

        await page.RefreshAsync();

        Assert.Equal("Não foi possível ler a lista de inicialização: sem acesso", page.Problem);
        Assert.Empty(page.Items);
        Assert.Equal("—", page.TotalText);
        Assert.False(page.IsEmpty);
    }

    [Fact]
    public async Task NoEntriesShowsTheFriendlyEmptyMessage()
    {
        var page = Create(new StubStartup());

        await page.RefreshAsync();

        Assert.True(page.IsEmpty);
        Assert.Equal("0", page.TotalText);
    }
}

public sealed class StartupTextsTests
{
    private static readonly ResourceManager Resources = new("M1vClock.App.Localization.Strings", typeof(Loc).Assembly);

    public static TheoryData<string> Languages => ["pt-BR", "en-US"];

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLocationAndStatusOfTheStartupListHasItsText(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var keys = Enum.GetValues<StartupLocation>().Select(l => $"Opt.Startup.Where.{l}")
            .Concat(["Opt.Startup.Status.Enabled", "Opt.Startup.Status.Disabled", "Opt.Startup.Status.Missing"]);

        var missing = keys.Where(k => string.IsNullOrWhiteSpace(Resources.GetString(k, culture))).ToList();

        Assert.True(missing.Count == 0, "faltam: " + string.Join(", ", missing));
    }
}
