using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Pages.Games;
using M1vClock.App.Services;
using M1vClock.Benchmarks.Results;
using M1vClock.Games.Estimate;
using M1vClock.Games.Library;

namespace M1vClock.App.Tests;

public sealed class FpsPageTests
{
    private sealed class FixedMachine(string cpu, string gpu) : IMachineProfile
    {
        public Task<BenchmarkMachine> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BenchmarkMachine(cpu, gpu, 16, 31.9, "Windows 11", "0.1.0"));
    }

    private sealed class InstalledGames(params string[] names) : IGameLibraryService
    {
        public Task<GameLibrary> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new GameLibrary(
            names.Select(n => new GameEntry(n, n, GamePlatform.Steam, null, null, GameState.Installed, null)).ToList(), []));
    }

    private sealed class StubReference : IReferenceFpsSource
    {
        public bool IsConfigured { get; set; }

        public Dictionary<string, ReferenceFps> Answers { get; } = new(StringComparer.Ordinal);

        public ReferenceFailure? FailWith { get; set; }

        public List<ReferenceQuery> Queries { get; } = [];

        public Task<ReferenceFps> GetAsync(ReferenceQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            if (FailWith is { } failure)
            {
                throw new ReferenceFpsException(failure, "falhou");
            }

            return Answers.TryGetValue(query.Game, out var answer)
                ? Task.FromResult(answer)
                : throw new ReferenceFpsException(ReferenceFailure.NotFound, "sem dados");
        }
    }

    private static async Task<(FpsPageViewModel Page, StubReference Reference, MemoryGameSettings Settings)> Open(string cpu = "AMD Ryzen 7 5700X 8-Core Processor", string gpu = "AMD Radeon RX 5500 XT")
    {
        var reference = new StubReference();
        var settings = new MemoryGameSettings();
        var page = new FpsPageViewModel(
            new FpsServices(new FixedMachine(cpu, gpu), new InstalledGames("Counter-Strike 2", "Grand Theft Auto V Enhanced", "VALORANT"), reference, settings),
            new CollectingLogger<FpsPageViewModel>());
        await page.RefreshAsync();
        return (page, reference, settings);
    }

    [Fact]
    public async Task ThisPcGetsAnEstimateForEveryGameWithTheInstalledOnesFirst()
    {
        var (page, _, _) = await Open();

        Assert.Equal("Ryzen 7 5700X", page.CpuText);
        Assert.Equal("RX 5500 XT", page.GpuText);
        Assert.Equal("Índice 77 · referência: Ryzen 7 7700X = 100", page.CpuDetail);
        Assert.Equal("Índice 36 · referência: RTX 4070 = 100", page.GpuDetail);
        Assert.Equal(["Counter-Strike 2", "Valorant", "GTA V"], page.Items.Take(3).Select(r => r.Title));
        Assert.Equal(FpsEstimator.Games.Count, page.Items.Count);
        var cs2 = page.Items[0];
        Assert.Equal("122", cs2.FpsText);
        Assert.Equal("1% low 92", cs2.LowText);
        Assert.Equal("Ótimo", cs2.RatingText);
        Assert.Equal(MetricState.Good, cs2.Tone);
        Assert.Equal("Estimativa local · quem limita: placa de vídeo", cs2.Detail);
        Assert.True(page.CanEstimate);
        Assert.False(page.HasProblem);
    }

    [Fact]
    public async Task ChangingResolutionOrFilteringUpdatesTheList()
    {
        var (page, _, _) = await Open();

        page.ResolutionIndex = 2;
        Assert.Equal("51", page.Items[0].FpsText);
        Assert.Equal("Fraco", page.Items[0].RatingText);

        page.OnlyInstalled = true;
        Assert.Equal(3, page.Items.Count);

        page.SearchText = "grand";
        Assert.True(page.IsEmpty);
    }

    [Fact]
    public async Task AGraphicsCardOutsideTheTableIsExplainedAndNothingIsInvented()
    {
        var (page, _, _) = await Open(gpu: "Microsoft Basic Display Adapter");

        Assert.False(page.CanEstimate);
        Assert.StartsWith("Sua placa de vídeo não está na tabela", page.Problem, StringComparison.Ordinal);
        Assert.Equal("Fora da tabela de desempenho", page.GpuDetail);
        Assert.All(page.Items, r => Assert.Equal("—", r.FpsText));
    }

    [Fact]
    public async Task TheKeyIsSavedEncryptedAndTheServiceAnswersReplaceTheEstimate()
    {
        var (page, reference, settings) = await Open();
        Assert.False(page.CanQuery);

        page.KeyText = "minha-chave";
        page.SaveKey();
        reference.IsConfigured = true;
        page.RemoveKey();
        page.KeyText = "minha-chave";
        page.SaveKey();
        Assert.Equal(string.Empty, page.KeyText);
        Assert.Equal("minha-chave", FpsApiKey.Read(settings, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));

        page.OnlyInstalled = true;
        reference.Answers["Counter-Strike 2"] = new ReferenceFps(187.4, 121.2, false);
        reference.Answers["Valorant"] = new ReferenceFps(301, 220, true);
        await page.QueryAsync();

        var cs2 = page.Items.Single(r => r.Title == "Counter-Strike 2");
        Assert.Equal("187", cs2.FpsText);
        Assert.Equal("HowManyFPS (sessões reais de jogadores)", cs2.Detail);
        Assert.Equal("HowManyFPS, consulta guardada (até 7 dias)", page.Items.Single(r => r.Title == "Valorant").Detail);
        Assert.StartsWith("Estimativa local", page.Items.Single(r => r.Title == "GTA V").Detail, StringComparison.Ordinal);
        Assert.Equal("A HowManyFPS respondeu para 2 de 3 jogos.", page.Notice);
        Assert.Equal(("Ryzen 7 5700X", "RX 5500 XT", EstimateResolution.FullHd), (reference.Queries[0].Cpu, reference.Queries[0].Gpu, reference.Queries[0].Resolution));
    }

    [Fact]
    public async Task ARefusedKeyStopsTheQueriesAtTheFirstGame()
    {
        var (page, reference, _) = await Open();
        reference.IsConfigured = true;
        page.KeyText = "x";
        page.SaveKey();
        reference.FailWith = ReferenceFailure.KeyRefused;

        await page.QueryAsync();

        Assert.Single(reference.Queries);
        Assert.Equal("A HowManyFPS recusou a chave.", page.Problem);
    }
}
