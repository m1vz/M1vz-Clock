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
using M1vClock.Optimize.Services;

namespace M1vClock.App.Tests;

public sealed class ServiceRowsTests
{
    private static ServiceEntry Entry(
        string name,
        string display,
        ServiceStartMode mode = ServiceStartMode.Manual,
        ServiceRunState state = ServiceRunState.Stopped,
        ServiceOrigin origin = ServiceOrigin.ThirdParty,
        string? company = null,
        string? description = null,
        ProtectionReason protection = ProtectionReason.None) =>
        new(name, display, description, state, mode, "LocalSystem", "C:\\x\\" + name + ".exe", origin, company, protection);

    [Fact]
    public void ARunningThirdPartyServiceShowsItsCompanyAccountAndThreeModeButtonsWithTheCurrentOneOff()
    {
        var row = new ServiceRowViewModel(Entry("chromoting", "Chrome Remote Desktop Service", ServiceStartMode.Automatic, ServiceRunState.Running, company: "Google LLC", description: "Permite o acesso remoto."), isElevated: true);

        Assert.Equal("Chrome Remote Desktop Service", row.Title);
        Assert.Equal("Permite o acesso remoto.", row.Detail);
        Assert.Equal("Google LLC · chromoting · LocalSystem", row.Meta);
        Assert.Equal("Rodando", row.Badge);
        Assert.Equal(MetricState.Good, row.Tone);
        Assert.Equal("Início: Automático", row.ModeText);
        Assert.Equal(["Automático", "Manual", "Desativado"], row.Options.Select(o => o.Text));
        Assert.Equal([false, true, true], row.Options.Select(o => o.IsEnabled));
        Assert.True(row.ShowOptions);
        Assert.True(row.CanChange);
        Assert.False(row.HasProtection);
    }

    [Fact]
    public void ADelayedAutomaticServiceAlreadyCountsAsAutomatic()
    {
        var row = new ServiceRowViewModel(Entry("wsearch", "Busca", ServiceStartMode.AutomaticDelayed), isElevated: true);

        Assert.Equal("Início: Automático (atrasado)", row.ModeText);
        Assert.False(row.Options[0].IsEnabled);
        Assert.True(row.Options[1].IsEnabled);
    }

    [Fact]
    public void WithoutAdministratorEveryButtonIsOffAndTheToolTipSaysWhy()
    {
        var row = new ServiceRowViewModel(Entry("x", "X"), isElevated: false);

        Assert.False(row.CanChange);
        Assert.All(row.Options, o => Assert.False(o.IsEnabled));
        Assert.All(row.Options, o => Assert.Equal("Precisa do m1vClock como administrador para mudar este programa.", o.ToolTip));
        Assert.True(row.ShowOptions);
    }

    [Theory]
    [InlineData(ProtectionReason.Windows, "Serviço do Windows: o m1vClock não muda o início dele.")]
    [InlineData(ProtectionReason.SecuritySoftware, "Antivírus ou segurança: o m1vClock não muda o início dele.")]
    [InlineData(ProtectionReason.AntiCheat, "Anti-cheat: desligar pode impedir jogos de abrirem. O m1vClock não muda o início dele.")]
    public void AProtectedServiceExplainsWhyAndOffersNoButtons(ProtectionReason reason, string text)
    {
        var row = new ServiceRowViewModel(Entry("x", "X", protection: reason), isElevated: true);

        Assert.Equal(text, row.ProtectionText);
        Assert.True(row.HasProtection);
        Assert.False(row.ShowOptions);
        Assert.False(row.CanChange);
        Assert.All(row.Options, o => Assert.Equal(text, o.ToolTip));
    }

    [Fact]
    public void AServiceWithAnUnknownStartIsNotOfferedButtonsEither()
    {
        var row = new ServiceRowViewModel(Entry("lsm", "LSM", ServiceStartMode.Unknown), isElevated: true);

        Assert.False(row.ShowOptions);
        Assert.Equal("Início: Desconhecido", row.ModeText);
    }

    [Fact]
    public void ADescriptionIsFlattenedAndCutAtOneHundredSixtyCharacters()
    {
        var text = "Primeira linha\r\nsegunda " + new string('x', 400);

        var shortened = ServiceRows.Shorten(text);

        Assert.StartsWith("Primeira linha segunda ", shortened, StringComparison.Ordinal);
        Assert.Equal(ServiceRows.DescriptionLength, shortened.Length);
        Assert.EndsWith("…", shortened, StringComparison.Ordinal);
        Assert.Equal(string.Empty, ServiceRows.Shorten("  "));
        Assert.Equal(string.Empty, ServiceRows.Shorten(null));
    }

    [Fact]
    public void TheDefaultFilterKeepsOnlyThirdPartyAndTheAllFilterKeepsEverything()
    {
        ServiceEntry[] services = [Entry("a", "A"), Entry("b", "B", origin: ServiceOrigin.Microsoft, protection: ProtectionReason.Windows), Entry("c", "C")];

        Assert.Equal(["A", "C"], ServiceRows.Filter(services, true, string.Empty, true, out var third).Select(r => r.Title));
        Assert.Equal(["A", "B", "C"], ServiceRows.Filter(services, false, string.Empty, true, out var all).Select(r => r.Title));
        Assert.Equal((2, 3), (third, all));
    }

    [Fact]
    public void TheSearchLooksInTheNameTheCompanyAndTheDescriptionIgnoringCase()
    {
        ServiceEntry[] services =
        [
            Entry("epicupdater", "Epic Games Updater", company: "Epic Games, Inc."),
            Entry("discordhelper", "Discord Helper", company: "Discord Inc.", description: "Atualiza o OVERLAY."),
            Entry("outro", "Outro"),
        ];

        Assert.Equal(["Epic Games Updater"], ServiceRows.Filter(services, false, "EPIC", true, out _).Select(r => r.Title));
        Assert.Equal(["Discord Helper"], ServiceRows.Filter(services, false, "overlay", true, out _).Select(r => r.Title));
        Assert.Equal(["Outro"], ServiceRows.Filter(services, false, "outro", true, out _).Select(r => r.Title));
        Assert.Empty(ServiceRows.Filter(services, false, "nada", true, out var none));
        Assert.Equal(0, none);
    }

    [Fact]
    public void OnlyTheFirstTwoHundredAreShownButTheMatchCountIsTheRealOne()
    {
        var services = Enumerable.Range(0, 277).Select(i => Entry($"s{i}", $"Serviço {i:000}")).ToList();

        var rows = ServiceRows.Filter(services, true, string.Empty, true, out var matching);

        Assert.Equal(ServiceRows.MaximumShown, rows.Count);
        Assert.Equal(277, matching);
    }
}

public sealed class ServicesPageViewModelTests : IDisposable
{
    private readonly DialogService _dialogs = new();
    private readonly List<DialogRequest> _shown = [];
    private bool _answer = true;

    public ServicesPageViewModelTests()
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

    private sealed class StubManager : IServiceManager
    {
        public ReadResult<ServiceReport> Report { get; set; } = ReadResult.Success(new ServiceReport([]));

        public List<(string Name, ServiceStartMode Mode)> Changes { get; } = [];

        public Func<ApplyOutcome>? Apply { get; set; }

        public Task<ReadResult<ServiceReport>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Report);

        public Task<ApplyOutcome> SetStartModeAsync(ServiceEntry entry, ServiceStartMode mode, CancellationToken cancellationToken)
        {
            Changes.Add((entry.Name, mode));
            return Task.FromResult(Apply!());
        }
    }

    private static ServiceEntry Entry(string name, string display, ServiceStartMode mode, ServiceRunState state, ServiceOrigin origin = ServiceOrigin.ThirdParty, ProtectionReason protection = ProtectionReason.None) =>
        new(name, display, null, state, mode, "LocalSystem", "C:\\x\\" + name + ".exe", origin, origin == ServiceOrigin.ThirdParty ? "Empresa" : "Microsoft Corporation", protection);

    private static ApplyOutcome Outcome(ChangeStatus status, string? error = null, RestorePointGateStatus gate = RestorePointGateStatus.CreatedNow) =>
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

    private static StubManager Realistic() => new()
    {
        Report = ReadResult.Success(new ServiceReport(
        [
            Entry("chromoting", "Chrome Remote Desktop Service", ServiceStartMode.Automatic, ServiceRunState.Running),
            Entry("EpicGamesUpdater", "Epic Games Updater", ServiceStartMode.Manual, ServiceRunState.Stopped),
            Entry("AVP21.27", "Kaspersky", ServiceStartMode.Automatic, ServiceRunState.Running, protection: ProtectionReason.SecuritySoftware),
            Entry("Spooler", "Spooler de Impressão", ServiceStartMode.Automatic, ServiceRunState.Running, ServiceOrigin.Microsoft, ProtectionReason.Windows),
        ])),
        Apply = () => Outcome(ChangeStatus.Applied),
    };

    private ServicesPageViewModel Create(StubManager manager, bool elevated = true) =>
        new(manager, _dialogs, new AppInfo("0.1.0", @"C:\dados", elevated), new CollectingLogger<ServicesPageViewModel>());

    [Fact]
    public async Task TheSummaryAndTheDefaultListShowOnlyTheThirdPartyServices()
    {
        var page = Create(Realistic());

        await page.RefreshAsync();

        Assert.Equal("4", page.TotalText);
        Assert.Equal("3", page.ThirdPartyText);
        Assert.Equal("2", page.AutoThirdText);
        Assert.Equal("3", page.RunningText);
        Assert.Equal(["Chrome Remote Desktop Service", "Epic Games Updater", "Kaspersky"], page.Items.Select(i => i.Title));
        Assert.False(page.HasShowing);
        Assert.False(page.IsEmpty);
    }

    [Fact]
    public async Task TheAllFilterAndTheSearchNarrowTheListWithoutReadingAgain()
    {
        var page = Create(Realistic());
        await page.RefreshAsync();

        page.FilterIndex = 1;
        Assert.Equal(4, page.Items.Count);
        page.SearchText = "spooler";
        Assert.Equal(["Spooler de Impressão"], page.Items.Select(i => i.Title));
        page.SearchText = "nada";
        Assert.Empty(page.Items);
        Assert.True(page.IsEmpty);
    }

    [Fact]
    public async Task ChangingTheStartAsksFirstWithTheFromToLineAndTheNextBootNote()
    {
        var manager = Realistic();
        var page = Create(manager);
        await page.RefreshAsync();
        var row = page.Items.Single(i => i.Title == "Chrome Remote Desktop Service");

        await page.ChangeAsync(row.Options.Single(o => o.Mode == ServiceStartMode.Manual));

        var request = Assert.Single(_shown);
        Assert.Equal("Mudar o início de Chrome Remote Desktop Service?", request.Title);
        Assert.Contains("passa a iniciar como Manual na próxima vez que o PC ligar", request.Message, StringComparison.Ordinal);
        Assert.Contains("não é parado nem iniciado agora", request.Message, StringComparison.Ordinal);
        Assert.Equal(["O início de Chrome Remote Desktop Service passa de Automático para Manual."], request.Changes);
        Assert.Contains("Desfazer na página Histórico", request.Undo, StringComparison.Ordinal);
        Assert.Equal([("chromoting", ServiceStartMode.Manual)], manager.Changes);
        Assert.StartsWith("O início de Chrome Remote Desktop Service agora é Manual.", page.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusingTheQuestionChangesNothing()
    {
        _answer = false;
        var manager = Realistic();
        var page = Create(manager);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items[0].Options[2]);

        Assert.Single(_shown);
        Assert.Empty(manager.Changes);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task WithoutAdministratorNoQuestionIsAskedAndNothingIsChanged()
    {
        var manager = Realistic();
        var page = Create(manager, elevated: false);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items[0].Options[1]);

        Assert.Empty(_shown);
        Assert.Empty(manager.Changes);
        Assert.True(page.NeedsAdministrator);
    }

    [Fact]
    public async Task TheCurrentModeButtonAndNullDoNothing()
    {
        var manager = Realistic();
        var page = Create(manager);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items[0].Options[0]);
        await page.ChangeAsync(null);

        Assert.Empty(_shown);
        Assert.Empty(manager.Changes);
    }

    [Fact]
    public async Task AFailedChangeShowsTheReasonAndNoSuccessMessage()
    {
        var manager = Realistic();
        manager.Apply = () => Outcome(ChangeStatus.ApplyFailed, "Acesso negado.");
        var page = Create(manager);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items[1].Options[2]);

        Assert.Equal("Não foi possível mudar Epic Games Updater: Acesso negado.", page.Problem);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task AMissingRestorePointIsMentionedWhileTheChangeStillCounts()
    {
        var manager = Realistic();
        manager.Apply = () => Outcome(ChangeStatus.Applied, gate: RestorePointGateStatus.Failed);
        var page = Create(manager);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items[1].Options[0]);

        Assert.Contains("não foi possível criar o ponto de restauração antes da mudança (sem proteção)", page.Notice, StringComparison.Ordinal);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task AfterAChangeTheListIsReadAgain()
    {
        var manager = Realistic();
        var page = Create(manager);
        await page.RefreshAsync();
        manager.Apply = () =>
        {
            manager.Report = ReadResult.Success(new ServiceReport([Entry("EpicGamesUpdater", "Epic Games Updater", ServiceStartMode.Disabled, ServiceRunState.Stopped)]));
            return Outcome(ChangeStatus.Applied);
        };

        await page.ChangeAsync(page.Items[1].Options[2]);

        var row = Assert.Single(page.Items);
        Assert.Equal("Início: Desativado", row.ModeText);
    }

    [Fact]
    public async Task ASourceThatFailsShowsTheProblemAndDashes()
    {
        var manager = new StubManager { Report = ReadResult.Failure<ServiceReport>("WMI quebrado") };
        var page = Create(manager);

        await page.RefreshAsync();

        Assert.Equal("Não foi possível ler a lista de serviços: WMI quebrado", page.Problem);
        Assert.Equal("—", page.TotalText);
        Assert.Empty(page.Items);
        Assert.False(page.IsEmpty);
    }

    [Fact]
    public async Task ABigListSaysHowManyAreNotShown()
    {
        var many = Enumerable.Range(0, 250).Select(i => Entry($"s{i}", $"Serviço {i:000}", ServiceStartMode.Manual, ServiceRunState.Stopped)).ToList();
        var page = Create(new StubManager { Report = ReadResult.Success(new ServiceReport(many)), Apply = () => Outcome(ChangeStatus.Applied) });

        await page.RefreshAsync();

        Assert.Equal(ServiceRows.MaximumShown, page.Items.Count);
        Assert.Equal("Mostrando 200 de 250 serviços. Use a busca para achar os outros.", page.ShowingText);
    }
}

public sealed class ServiceTextsTests
{
    private static readonly ResourceManager Resources = new("M1vClock.App.Localization.Strings", typeof(Loc).Assembly);

    public static TheoryData<string> Languages => ["pt-BR", "en-US"];

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryModeStateAndProtectionOfAServiceHasItsText(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var keys = Enum.GetValues<ServiceStartMode>().Select(m => $"Opt.Services.Mode.{m}")
            .Concat(Enum.GetValues<ServiceRunState>().Select(s => $"Opt.Services.State.{s}"))
            .Concat(Enum.GetValues<ProtectionReason>().Where(p => p != ProtectionReason.None).Select(p => $"Opt.Services.Protection.{p}"));

        var missing = keys.Where(k => string.IsNullOrWhiteSpace(Resources.GetString(k, culture))).ToList();

        Assert.True(missing.Count == 0, "faltam: " + string.Join(", ", missing));
    }
}
