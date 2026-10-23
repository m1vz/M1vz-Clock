using System.Globalization;
using System.IO;
using System.Resources;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Dialogs;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Pages.Optimize;
using M1vClock.Optimize.Cleanup;

namespace M1vClock.App.Tests;

public sealed class CleanupRowsTests
{
    private static CleanupScan Scan(string id, int files, long bytes, bool admin = false, bool rebuilds = false, int unreadable = 0) =>
        new(new CleanupCategory(id, [@"C:\x\y\z"], admin, TimeSpan.FromDays(1), rebuilds), files, bytes, unreadable);

    [Fact]
    public void TheRealUserTempScanOfThisPcShowsItsSizeAndCount()
    {
        var row = new CleanupRowViewModel(Scan(CleanupCatalog.UserTemp, 5097, 246_000_000), isElevated: false);

        Assert.Equal("Arquivos temporários do seu usuário", row.Title);
        Assert.Equal("234,6 MB · 5.097 arquivo(s)", row.SizeText);
        Assert.True(row.CanClean);
        Assert.False(row.HasBadge);
        Assert.Equal("Limpar", row.ActionToolTip);
    }

    [Fact]
    public void AnEmptyPlaceSaysNothingToCleanAndOffersNoButton()
    {
        var row = new CleanupRowViewModel(Scan(CleanupCatalog.CrashDumps, 0, 0), isElevated: true);

        Assert.Equal("Nada para limpar aqui.", row.SizeText);
        Assert.False(row.CanClean);
    }

    [Fact]
    public void APlaceThatNeedsAdministratorIsLockedWithoutItAndSaysSo()
    {
        var scan = Scan(CleanupCatalog.WindowsTemp, 10, 1000, admin: true);

        var user = new CleanupRowViewModel(scan, isElevated: false);
        var admin = new CleanupRowViewModel(scan, isElevated: true);

        Assert.False(user.CanClean);
        Assert.True(user.NeedsAdministrator);
        Assert.Equal("Precisa de administrador", user.Badge);
        Assert.Equal(MetricState.Warning, user.Tone);
        Assert.Equal("Abra o m1vClock como administrador para limpar este lugar.", user.ActionToolTip);
        Assert.True(admin.CanClean);
        Assert.False(admin.HasBadge);
    }

    [Fact]
    public void ACacheThatRebuildsItselfSaysSoAndFilesThatCouldNotBeMeasuredAreMentioned()
    {
        var row = new CleanupRowViewModel(Scan(CleanupCatalog.ShaderCache, 88, 167_600_000, rebuilds: true, unreadable: 3), isElevated: false);

        Assert.Equal("Recria sozinho", row.Badge);
        Assert.Equal("3 arquivo(s) não puderam ser medidos.", row.UnreadableText);
        Assert.True(row.HasUnreadable);
    }

    [Fact]
    public void EveryCategoryOfTheCatalogHasATitleAndADetailInBothLanguages()
    {
        var resources = new ResourceManager("M1vClock.App.Localization.Strings", typeof(Loc).Assembly);
        foreach (var language in new[] { "pt-BR", "en-US" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            foreach (var id in CleanupCatalog.Ids)
            {
                Assert.False(string.IsNullOrWhiteSpace(resources.GetString($"Opt.Cleanup.Cat.{id}.Title", culture)), $"{language} {id} título");
                Assert.False(string.IsNullOrWhiteSpace(resources.GetString($"Opt.Cleanup.Cat.{id}.Detail", culture)), $"{language} {id} detalhe");
            }
        }
    }
}

public sealed class CleanupPageViewModelTests : IDisposable
{
    private readonly DialogService _dialogs = new();
    private readonly List<DialogRequest> _shown = [];
    private bool _answer = true;

    public CleanupPageViewModelTests()
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

    private sealed class StubCleanup : ICleanupService
    {
        public CleanupSummary Summary { get; set; } = new([]);

        public List<string> Cleaned { get; } = [];

        public Func<string, CleanupOutcome>? Clean { get; set; }

        public IReadOnlyList<CleanupCategory> Categories => Summary.Scans.Select(s => s.Category).ToList();

        public Task<CleanupSummary> ScanAsync(CancellationToken cancellationToken) => Task.FromResult(Summary);

        public Task<CleanupOutcome> CleanAsync(string categoryId, CancellationToken cancellationToken)
        {
            Cleaned.Add(categoryId);
            return Task.FromResult(Clean!(categoryId));
        }
    }

    private static CleanupScan Scan(string id, int files, long bytes, bool admin = false) =>
        new(new CleanupCategory(id, [@"C:\x\y\z"], admin, TimeSpan.FromDays(1), false), files, bytes, 0);

    private static StubCleanup Realistic()
    {
        var cleanup = new StubCleanup
        {
            Summary = new CleanupSummary(
            [
                Scan(CleanupCatalog.UserTemp, 5097, 246_000_000),
                Scan(CleanupCatalog.WindowsTemp, 0, 0, admin: true),
                Scan(CleanupCatalog.CrashDumps, 2, 19_600_000),
                Scan(CleanupCatalog.UpdateDownloads, 40, 5_000_000, admin: true),
            ]),
        };
        cleanup.Clean = id => new CleanupOutcome(cleanup.Summary.Scans.Single(s => s.Category.Id == id).Category, 10, 1_048_576, 0);
        return cleanup;
    }

    private CleanupPageViewModel Create(StubCleanup cleanup, bool elevated = false) =>
        new(cleanup, _dialogs, new AppInfo("0.1.0", @"C:\dados", elevated), new CollectingLogger<CleanupPageViewModel>());

    [Fact]
    public async Task TheSummaryAddsUpEverythingTheScanFound()
    {
        var page = Create(Realistic());

        await page.RefreshAsync();

        Assert.Equal("253,3 MB", page.TotalText);
        Assert.Equal("5.099", page.FilesText);
        Assert.Equal("2", page.PlacesText);
        Assert.Equal(4, page.Items.Count);
        Assert.True(page.CanCleanAll);
        Assert.False(page.IsLoading);
    }

    [Fact]
    public async Task ElevatedTheSummaryAlsoCountsTheWindowsPlaces()
    {
        var page = Create(Realistic(), elevated: true);

        await page.RefreshAsync();

        Assert.Equal("258,06 MB", page.TotalText);
        Assert.Equal("5.139", page.FilesText);
        Assert.Equal("3", page.PlacesText);
    }

    [Fact]
    public async Task CleaningOnePlaceAsksFirstWithADestructiveTonePlainWordsAndNoUndo()
    {
        var cleanup = Realistic();
        var page = Create(cleanup);
        await page.RefreshAsync();

        await page.CleanAsync(page.Items.Single(i => i.Id == CleanupCatalog.UserTemp));

        var request = Assert.Single(_shown);
        Assert.Equal(DialogTone.Destructive, request.Tone);
        Assert.Equal("Apagar estes arquivos?", request.Title);
        Assert.Contains("não vão para a lixeira e não dá para desfazer", request.Message, StringComparison.Ordinal);
        Assert.Equal(["Arquivos temporários do seu usuário: 234,6 MB · 5.097 arquivo(s)"], request.Changes);
        Assert.Equal("Não há como desfazer. Se tiver dúvida, cancele.", request.Undo);
        Assert.Equal("Apagar", request.ConfirmText);
        Assert.Equal([CleanupCatalog.UserTemp], cleanup.Cleaned);
        Assert.Equal("10 arquivo(s) apagados e 1 MB liberados.", page.Notice);
    }

    [Fact]
    public async Task CleaningEverythingTakesOnlyThePlacesThatCanBeCleaned()
    {
        var cleanup = Realistic();
        var page = Create(cleanup, elevated: false);
        await page.RefreshAsync();

        await page.CleanAllCommand.ExecuteAsync(null);

        Assert.Equal([CleanupCatalog.UserTemp, CleanupCatalog.CrashDumps], cleanup.Cleaned);
        Assert.Equal(2, Assert.Single(_shown).Changes.Count);
        Assert.Equal("20 arquivo(s) apagados e 2 MB liberados.", page.Notice);
    }

    [Fact]
    public async Task WhenElevatedTheWindowsPlacesCountToo()
    {
        var cleanup = Realistic();
        var page = Create(cleanup, elevated: true);
        await page.RefreshAsync();

        await page.CleanAllCommand.ExecuteAsync(null);

        Assert.Equal([CleanupCatalog.UserTemp, CleanupCatalog.CrashDumps, CleanupCatalog.UpdateDownloads], cleanup.Cleaned);
    }

    [Fact]
    public async Task RefusingTheQuestionDeletesNothing()
    {
        _answer = false;
        var cleanup = Realistic();
        var page = Create(cleanup);
        await page.RefreshAsync();

        await page.CleanAsync(page.Items[0]);
        await page.CleanAllCommand.ExecuteAsync(null);

        Assert.Equal(2, _shown.Count);
        Assert.Empty(cleanup.Cleaned);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task APlaceThatCannotBeCleanedIsNeverSentToTheService()
    {
        var cleanup = Realistic();
        var page = Create(cleanup, elevated: false);
        await page.RefreshAsync();

        await page.CleanAsync(page.Items.Single(i => i.Id == CleanupCatalog.WindowsTemp));
        await page.CleanAsync(page.Items.Single(i => i.Id == CleanupCatalog.UpdateDownloads));
        await page.CleanAsync(null);

        Assert.Empty(_shown);
        Assert.Empty(cleanup.Cleaned);
    }

    [Fact]
    public async Task FilesThatStayBecauseTheyAreInUseAreMentioned()
    {
        var cleanup = Realistic();
        cleanup.Clean = id => new CleanupOutcome(cleanup.Summary.Scans.Single(s => s.Category.Id == id).Category, 90, 5_242_880, 7);
        var page = Create(cleanup);
        await page.RefreshAsync();

        await page.CleanAsync(page.Items[0]);

        Assert.Equal("90 arquivo(s) apagados e 5 MB liberados. 7 ficaram porque estão em uso ou protegidos.", page.Notice);
    }

    [Fact]
    public async Task AnErrorWhileCleaningShowsTheProblemAndStopsTheCleaningFlag()
    {
        var cleanup = Realistic();
        cleanup.Clean = _ => throw new UnauthorizedAccessException("Acesso negado.");
        var page = Create(cleanup);
        await page.RefreshAsync();

        await page.CleanAsync(page.Items[0]);

        Assert.Equal("Não foi possível limpar: Acesso negado.", page.Problem);
        Assert.False(page.IsCleaning);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task AfterCleaningTheScanRunsAgain()
    {
        var cleanup = Realistic();
        var page = Create(cleanup);
        await page.RefreshAsync();
        cleanup.Clean = id =>
        {
            cleanup.Summary = new CleanupSummary([Scan(CleanupCatalog.UserTemp, 0, 0)]);
            return new CleanupOutcome(cleanup.Summary.Scans[0].Category, 5097, 246_000_000, 0);
        };

        await page.CleanAsync(page.Items[0]);

        Assert.Single(page.Items);
        Assert.Equal("0 B", page.TotalText);
        Assert.False(page.CanCleanAll);
    }

    [Fact]
    public async Task NothingToCleanAnywhereLeavesTheCleanAllButtonOff()
    {
        var cleanup = new StubCleanup { Summary = new CleanupSummary([Scan(CleanupCatalog.UserTemp, 0, 0)]) };
        var page = Create(cleanup);

        await page.RefreshAsync();

        Assert.False(page.CanCleanAll);
        Assert.Equal("0", page.PlacesText);
    }
}
