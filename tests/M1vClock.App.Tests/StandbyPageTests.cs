using M1vClock.App.Dialogs;
using M1vClock.App.Hosting;
using M1vClock.App.Pages.Optimize;
using M1vClock.Core.Results;
using M1vClock.Optimize.Memory;

namespace M1vClock.App.Tests;

public sealed class StandbyPageViewModelTests : IDisposable
{
    private readonly DialogService _dialogs = new();
    private readonly List<DialogRequest> _shown = [];
    private bool _answer = true;

    public StandbyPageViewModelTests()
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

    private sealed class StubMemory : IMemoryListService
    {
        public ReadResult<MemoryListSnapshot> Snapshot { get; set; } = ReadResult.Success(ThisPc());

        public PurgeResult Purge { get; set; } = new(PurgeStatus.Done, 3_907_000_000, 0, null);

        public int Purges { get; private set; }

        public Task<ReadResult<MemoryListSnapshot>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);

        public Task<PurgeResult> PurgeStandbyAsync(CancellationToken cancellationToken)
        {
            Purges++;
            return Task.FromResult(Purge);
        }
    }

    private static MemoryListSnapshot ThisPc() => new(
        PageSize: 4096,
        TotalPages: 4_164_000,
        ZeroPages: 2000,
        FreePages: 365_000,
        ModifiedPages: 25_000,
        ModifiedNoWritePages: 0,
        StandbyPagesByPriority: [1000, 2000, 3000, 4000, 5000, 6000, 7000, 926_000]);

    private StandbyPageViewModel Create(StubMemory memory, bool elevated = true) =>
        new(memory, _dialogs, new AppInfo("0.1.0", @"C:\dados", elevated), new CollectingLogger<StandbyPageViewModel>());

    [Fact]
    public async Task TheMemoryOfThisPcIsShownAsTotalUsedStandbyAndFree()
    {
        var page = Create(new StubMemory());

        await page.RefreshAsync();

        Assert.Equal("15,88 GB", page.TotalText);
        Assert.Equal("3,64 GB", page.StandbyText);
        Assert.Equal("1,4 GB", page.FreeText);
        Assert.StartsWith("Disponível para programas: 5,", page.AvailableText, StringComparison.Ordinal);
        Assert.StartsWith("Modificada, esperando gravar no disco: 97,", page.ModifiedText, StringComparison.Ordinal);
        Assert.False(page.IsLoading);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task WithoutAdministratorThePurgeIsOffAndNothingIsAsked()
    {
        var memory = new StubMemory();
        var page = Create(memory, elevated: false);
        await page.RefreshAsync();

        await page.PurgeAsync();

        Assert.False(page.CanPurge);
        Assert.True(page.NeedsAdministrator);
        Assert.Empty(_shown);
        Assert.Equal(0, memory.Purges);
    }

    [Fact]
    public async Task ThePurgeAsksFirstExplainingItOnlyDropsTheCacheAndThenReportsWhatWasFreed()
    {
        var memory = new StubMemory { Purge = new PurgeResult(PurgeStatus.Done, 3_907_000_000, 80_000_000, null) };
        var page = Create(memory);
        await page.RefreshAsync();

        await page.PurgeAsync();

        var request = Assert.Single(_shown);
        Assert.Equal("Esvaziar a memória em espera?", request.Title);
        Assert.Contains("Só o cache do Windows é descartado", request.Message, StringComparison.Ordinal);
        Assert.Equal(["Cerca de 3,64 GB de cache serão descartados."], request.Changes);
        Assert.Contains("enche o cache de novo sozinho", request.Undo, StringComparison.Ordinal);
        Assert.Equal(1, memory.Purges);
        Assert.StartsWith("Foram liberados 3,56 GB de memória em espera", page.Notice, StringComparison.Ordinal);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task RefusingTheQuestionDropsNothing()
    {
        _answer = false;
        var memory = new StubMemory();
        var page = Create(memory);
        await page.RefreshAsync();

        await page.PurgeAsync();

        Assert.Single(_shown);
        Assert.Equal(0, memory.Purges);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task ACacheThatWasAlreadyEmptySaysSoInsteadOfClaimingGigabytes()
    {
        var memory = new StubMemory { Purge = new PurgeResult(PurgeStatus.Done, 100_000_000, 100_000_000, null) };
        var page = Create(memory);
        await page.RefreshAsync();

        await page.PurgeAsync();

        Assert.Equal("A memória em espera já estava quase vazia.", page.Notice);
    }

    [Fact]
    public async Task WindowsRefusingThePurgeKeepsTheProblemEvenAfterTheReload()
    {
        var memory = new StubMemory { Purge = new PurgeResult(PurgeStatus.NeedsAdministrator, 1, 1, null) };
        var page = Create(memory);
        await page.RefreshAsync();

        await page.PurgeAsync();

        Assert.Equal("O Windows não deixou esvaziar a memória em espera. Abra o m1vClock como administrador.", page.Problem);
        Assert.False(page.HasNotice);
    }

    [Fact]
    public async Task APurgeThatFailedShowsTheReason()
    {
        var memory = new StubMemory { Purge = new PurgeResult(PurgeStatus.Failed, 1, 1, "código C0000005") };
        var page = Create(memory);
        await page.RefreshAsync();

        await page.PurgeAsync();

        Assert.Equal("Não foi possível esvaziar a memória em espera: código C0000005", page.Problem);
    }

    [Fact]
    public async Task ANearlyEmptyStandbyListOffersNoPurge()
    {
        var memory = new StubMemory { Snapshot = ReadResult.Success(ThisPc() with { StandbyPagesByPriority = [0, 0, 0, 0, 0, 0, 0, 0] }) };
        var page = Create(memory);

        await page.RefreshAsync();

        Assert.False(page.CanPurge);
    }

    [Fact]
    public async Task AReadThatFailedShowsTheProblemAndDashesAndClearsItWhenItWorksAgain()
    {
        var memory = new StubMemory { Snapshot = ReadResult.Failure<MemoryListSnapshot>("sem leitura") };
        var page = Create(memory);

        await page.RefreshAsync();

        Assert.Equal("Não foi possível ler a memória do Windows: sem leitura", page.Problem);
        Assert.Equal("—", page.TotalText);
        Assert.False(page.CanPurge);

        memory.Snapshot = ReadResult.Success(ThisPc());
        await page.RefreshAsync();

        Assert.False(page.HasProblem);
        Assert.Equal("15,88 GB", page.TotalText);
        Assert.True(page.CanPurge);
    }
}
