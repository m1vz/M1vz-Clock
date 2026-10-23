using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Dialogs;
using M1vClock.App.Pages.Optimize;
using M1vClock.Core.Results;
using M1vClock.Core.Security;
using M1vClock.Optimize.Tweaks;

namespace M1vClock.App.Tests;

public sealed class TweaksPageViewModelTests : IDisposable
{
    private readonly DialogService _dialogs = new();
    private readonly List<DialogRequest> _shown = [];
    private bool _answer = true;

    public TweaksPageViewModelTests()
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

    private sealed class StubTweaks : ITweakService
    {
        public ReadResult<TweakReport> Report { get; set; } = ReadResult.Success(ThisPc());

        public Dictionary<string, TweakPlan?> Plans { get; } = new()
        {
            ["gamedvr"] = new TweakPlan("gamedvr", true, false, false,
            [
                new TweakChangePreview(@"HKCU\System\GameConfigStore\GameDVR_Enabled", "1", "0"),
                new TweakChangePreview(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled", null, "0"),
            ]),
            ["hags"] = new TweakPlan("hags", true, true, true, [new TweakChangePreview(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode", null, "2")]),
            ["mouse"] = new TweakPlan("mouse", false, false, false, [new TweakChangePreview(@"HKCU\Control Panel\Mouse\MouseSpeed", "0", "1")]),
        };

        public TweakOutcome Outcome { get; set; } = new(true, TweakState.On, null, false, false, true, null);

        public TweakRestoreOutcome Restored { get; set; } = new(3, []);

        public List<(string Id, bool TurnOn)> Sets { get; } = [];

        public int Restores { get; private set; }

        public Task<ReadResult<TweakReport>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Report);

        public TweakPlan? Plan(string id, bool turnOn) => Plans.GetValueOrDefault(id);

        public Task<TweakOutcome> SetAsync(string id, bool turnOn, CancellationToken cancellationToken)
        {
            Sets.Add((id, turnOn));
            return Task.FromResult(Outcome);
        }

        public Task<TweakRestoreOutcome> RestoreOriginalsAsync(CancellationToken cancellationToken)
        {
            Restores++;
            return Task.FromResult(Restored);
        }
    }

    private static TweakReport ThisPc() => new(
    [
        new TweakEntry("power", TweakState.Off, TweakIssue.None, false, false, true, false),
        new TweakEntry("gamemode", TweakState.Off, TweakIssue.None, false, false, true, false),
        new TweakEntry("gamedvr", TweakState.Off, TweakIssue.None, false, false, true, false),
        new TweakEntry("hags", TweakState.Off, TweakIssue.None, true, true, true, false),
        new TweakEntry("windowed", TweakState.Off, TweakIssue.None, false, false, true, false),
        new TweakEntry("mouse", TweakState.On, TweakIssue.None, false, false, true, false),
    ]);

    private static ElevationInfo Elevation(bool elevated) => new(elevated, "S-1-5-21-1-2-3-1001", @"PC-DE-TESTE\joao");

    private TweaksPageViewModel Create(StubTweaks tweaks, bool elevated = true) =>
        new(tweaks, _dialogs, Elevation(elevated), new CollectingLogger<TweaksPageViewModel>());

    [Fact]
    public async Task ThePageShowsTheSixTweaksTheirStatesAndTheSummary()
    {
        var page = Create(new StubTweaks());

        await page.RefreshAsync();

        Assert.Equal(["power", "gamemode", "gamedvr", "hags", "windowed", "mouse"], page.Items.Select(r => r.Id));
        Assert.Equal("6", page.TotalText);
        Assert.Equal("1", page.OnText);
        Assert.Equal("5", page.OffText);
        Assert.Equal("0", page.ChangedText);
        var mouse = page.Items.Single(r => r.Id == "mouse");
        Assert.Equal("Ligado", mouse.StateText);
        Assert.Equal(MetricState.Good, mouse.Tone);
        Assert.Equal("Desligar", mouse.ActionText);
        Assert.Equal("Ligar", page.Items[0].ActionText);
        Assert.Equal("Desligado", page.Items[0].StateText);
        Assert.False(page.CanRestore);
        Assert.False(page.IsLoading);
    }

    [Fact]
    public async Task OnlyTheAccountNameIsShownNeverTheComputerName()
    {
        var page = Create(new StubTweaks());

        Assert.Equal("Os ajustes de usuário valem para a conta joao.", page.UserNote);
        await page.RefreshAsync();
        Assert.DoesNotContain("PC-DE-TESTE", page.UserNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATweakThatNeedsAdministratorIsOffWithoutItAndTheCardSaysSo()
    {
        var page = Create(new StubTweaks(), elevated: false);
        await page.RefreshAsync();

        var hags = page.Items.Single(r => r.Id == "hags");

        Assert.False(hags.CanChange);
        Assert.Equal("Precisa do m1vClock como administrador.", hags.ActionToolTip);
        Assert.Equal("precisa reiniciar o PC · precisa do m1vClock como administrador", hags.Meta);
        Assert.True(page.Items.Single(r => r.Id == "gamedvr").CanChange);

        await page.ChangeAsync(hags);
        Assert.Empty(_shown);
    }

    [Fact]
    public async Task TheSameTweakIsEnabledWhenElevated()
    {
        var page = Create(new StubTweaks(), elevated: true);
        await page.RefreshAsync();

        Assert.True(page.Items.Single(r => r.Id == "hags").CanChange);
    }

    [Fact]
    public async Task ChangingATweakAsksFirstListingEveryValueThenAppliesAndSaysItDid()
    {
        var tweaks = new StubTweaks();
        var page = Create(tweaks);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(r => r.Id == "gamedvr"));

        var request = Assert.Single(_shown);
        Assert.Equal("Ligar este ajuste?", request.Title);
        Assert.StartsWith("Ajuste: Desativar a gravação em segundo plano (Game DVR).", request.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("reiniciar", request.Message, StringComparison.Ordinal);
        Assert.Equal(
        [
            @"HKCU\System\GameConfigStore\GameDVR_Enabled: 1 → 0",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled: ausente → 0",
        ], request.Changes);
        Assert.Contains("Restaurar originais", request.Undo, StringComparison.Ordinal);
        Assert.Equal(DialogTone.Neutral, request.Tone);
        Assert.Equal([("gamedvr", true)], tweaks.Sets);
        Assert.Equal("Ajuste ligado: Desativar a gravação em segundo plano (Game DVR).", page.Notice);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task TurningOffAsksToTurnOffAndTheRestartNoteAppearsOnlyWhereNeeded()
    {
        var tweaks = new StubTweaks();
        var page = Create(tweaks);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(r => r.Id == "mouse"));
        await page.ChangeAsync(page.Items.Single(r => r.Id == "hags"));

        Assert.Equal("Desligar este ajuste?", _shown[0].Title);
        Assert.Equal("Ligar este ajuste?", _shown[1].Title);
        Assert.Contains("só vale depois de reiniciar o PC", _shown[1].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("reiniciar", _shown[0].Message, StringComparison.Ordinal);
        Assert.Equal([("mouse", false), ("hags", true)], tweaks.Sets);
    }

    [Fact]
    public async Task RefusingTheQuestionChangesNothing()
    {
        _answer = false;
        var tweaks = new StubTweaks();
        var page = Create(tweaks);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(r => r.Id == "gamedvr"));

        Assert.Single(_shown);
        Assert.Empty(tweaks.Sets);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task AFailedChangeShowsTheReasonAndNoSuccess()
    {
        var tweaks = new StubTweaks { Outcome = TweakOutcome.Failure(TweakState.Off, "Acesso negado.") };
        var page = Create(tweaks);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(r => r.Id == "gamedvr"));

        Assert.Equal("Não foi possível mudar Desativar a gravação em segundo plano (Game DVR): Acesso negado.", page.Problem);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task TheNoticeMentionsTheRestartTheMouseThatWasNotAppliedAndTheMissingRestorePoint()
    {
        var tweaks = new StubTweaks { Outcome = new TweakOutcome(true, TweakState.On, null, true, true, false, "sem permissão") };
        var page = Create(tweaks);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(r => r.Id == "hags"));

        Assert.Contains("Reinicie o PC para valer.", page.Notice, StringComparison.Ordinal);
        Assert.Contains("não aplicou o mouse na hora", page.Notice, StringComparison.Ordinal);
        Assert.Contains("sem permissão", page.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATweakWithNothingLeftToChangeAsksNothingAndJustReloads()
    {
        var tweaks = new StubTweaks();
        tweaks.Plans["gamedvr"] = null;
        var page = Create(tweaks);
        await page.RefreshAsync();

        await page.ChangeAsync(page.Items.Single(r => r.Id == "gamedvr"));

        Assert.Empty(_shown);
        Assert.Empty(tweaks.Sets);
    }

    [Fact]
    public async Task RestoreOriginalsIsOffUntilTheAppHasChangedSomething()
    {
        var tweaks = new StubTweaks();
        var page = Create(tweaks);
        await page.RefreshAsync();

        await page.RestoreAsync();

        Assert.False(page.CanRestore);
        Assert.Empty(_shown);
        Assert.Equal(0, tweaks.Restores);
    }

    [Fact]
    public async Task RestoreOriginalsAsksThenUndoesAndCountsTheChanges()
    {
        var changed = new TweakReport([new TweakEntry("gamedvr", TweakState.On, TweakIssue.None, false, false, true, true), new TweakEntry("mouse", TweakState.On, TweakIssue.None, false, false, true, true)]);
        var tweaks = new StubTweaks { Report = ReadResult.Success(changed) };
        var page = Create(tweaks);
        await page.RefreshAsync();

        Assert.True(page.CanRestore);
        Assert.Equal("2", page.ChangedText);

        await page.RestoreAsync();

        var request = Assert.Single(_shown);
        Assert.Equal("Restaurar os valores originais?", request.Title);
        Assert.Equal(["Ajustes com mudanças do m1vClock: 2"], request.Changes);
        Assert.Equal(1, tweaks.Restores);
        Assert.Equal("Foram desfeitas 3 alterações.", page.Notice);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task ARestoreThatCouldNotUndoEverythingKeepsTheProblemAfterTheReload()
    {
        var changed = new TweakReport([new TweakEntry("gamedvr", TweakState.On, TweakIssue.None, false, false, true, true)]);
        var tweaks = new StubTweaks { Report = ReadResult.Success(changed), Restored = new TweakRestoreOutcome(1, ["Ligar ajuste: Modo de Jogo: Acesso negado."]) };
        var page = Create(tweaks);
        await page.RefreshAsync();

        await page.RestoreAsync();

        Assert.Equal("Foram desfeitas 1 alterações.", page.Notice);
        Assert.Equal("Algumas alterações não puderam ser desfeitas: Ligar ajuste: Modo de Jogo: Acesso negado.", page.Problem);
    }

    [Fact]
    public async Task AnIssueIsExplainedOnTheCardAndOffersNoAction()
    {
        var report = new TweakReport([new TweakEntry("power", TweakState.Unavailable, TweakIssue.NoHighPerformancePlan, false, false, false, false)]);
        var page = Create(new StubTweaks { Report = ReadResult.Success(report) });

        await page.RefreshAsync();

        var row = Assert.Single(page.Items);
        Assert.Equal("Indisponível", row.StateText);
        Assert.Equal("Este Windows não tem o plano Alto desempenho, e o m1vClock não cria planos de energia.", row.IssueText);
        Assert.True(row.HasIssue);
        Assert.False(row.CanChange);
    }

    [Fact]
    public async Task AReadThatFailedShowsTheProblemAndDashesAndClearsItWhenItWorksAgain()
    {
        var tweaks = new StubTweaks { Report = ReadResult.Failure<TweakReport>("sem acesso") };
        var page = Create(tweaks);

        await page.RefreshAsync();

        Assert.Equal("Não foi possível ler os ajustes: sem acesso", page.Problem);
        Assert.Equal("—", page.TotalText);
        Assert.Empty(page.Items);

        tweaks.Report = ReadResult.Success(ThisPc());
        await page.RefreshAsync();

        Assert.False(page.HasProblem);
        Assert.Equal("6", page.TotalText);
    }
}
