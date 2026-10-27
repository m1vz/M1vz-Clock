using System.Net;
using System.Text;
using System.Text.Json;
using M1vClock.Games.Estimate;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Games.Tests;

public sealed class FpsEstimatorTests
{
    private static readonly EstimateSettings FullHdHigh = new(EstimateResolution.FullHd, EstimatePreset.High, EstimateUpscaling.Off);

    private static CatalogGame Game(string name) => FpsEstimator.Games.Single(g => g.Name == name);

    [Fact]
    public void TheCpuAndGpuOfThisPcAreInTheTables()
    {
        Assert.Equal(36, FpsEstimator.GpuIndexOf("AMD Radeon RX 5500 XT"));
        Assert.Equal(77, FpsEstimator.CpuIndexOf("AMD Ryzen 7 5700X 8-Core Processor             "));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", 136)]
    [InlineData("NVIDIA GeForce RTX 4070", 100)]
    [InlineData("NVIDIA GeForce RTX 3060 Ti", 78)]
    [InlineData("AMD Radeon RX 6600 XT", 66)]
    [InlineData("AMD Radeon RX 6600", 58)]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", 80)]
    public void TheLongestMatchingGpuNameWins(string name, double expected) =>
        Assert.Equal(expected, FpsEstimator.GpuIndexOf(name));

    [Theory]
    [InlineData("AMD Ryzen 7 5700X3D 8-Core Processor", 90)]
    [InlineData("AMD Ryzen 5 5600X 6-Core Processor", 76)]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700K", 108)]
    [InlineData("12th Gen Intel(R) Core(TM) i5-12600K", 90)]
    public void TheLongestMatchingCpuNameWins(string name, double expected) =>
        Assert.Equal(expected, FpsEstimator.CpuIndexOf(name));

    [Fact]
    public void UnknownHardwareHasNoIndex()
    {
        Assert.Null(FpsEstimator.GpuIndexOf("Microsoft Basic Display Adapter"));
        Assert.Null(FpsEstimator.CpuIndexOf("Qualcomm Snapdragon X Elite"));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", "RTX 4070")]
    [InlineData("AMD Radeon RX 5500 XT", "RX 5500 XT")]
    [InlineData("AMD Radeon(TM) Graphics", "Radeon Graphics")]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", "Arc B580")]
    public void GpuNamesAreShortenedTheWayTheServiceExpects(string name, string expected) =>
        Assert.Equal(expected, FpsEstimator.ShortGpuName(name));

    [Theory]
    [InlineData("AMD Ryzen 7 5700X 8-Core Processor             ", "Ryzen 7 5700X")]
    [InlineData("13th Gen Intel(R) Core(TM) i7-13700K", "Core i7-13700K")]
    [InlineData("Intel(R) Core(TM) i5-12400F CPU @ 2.50GHz", "Core i5-12400F")]
    public void CpuNamesAreShortenedTheWayTheServiceExpects(string name, string expected) =>
        Assert.Equal(expected, FpsEstimator.ShortCpuName(name));

    [Fact]
    public void TheReferenceMachineGetsTheCatalogValueLimitedByTheSlowerPart()
    {
        var cs2 = FpsEstimator.Estimate(Game("Counter-Strike 2"), 100, 100, FullHdHigh);
        var trucks = FpsEstimator.Estimate(Game("Euro Truck Simulator 2"), 100, 100, FullHdHigh);

        Assert.Equal(new FpsEstimate(340, 255, FpsLimiter.Gpu), cs2);
        Assert.Equal(FpsLimiter.Cpu, trucks.Limiter);
        Assert.Equal(160, trucks.Average);
    }

    [Fact]
    public void AGameWithAFrameCapNeverGoesAboveIt()
    {
        var estimate = FpsEstimator.Estimate(Game("Elden Ring"), 265, 140, FullHdHigh);

        Assert.Equal(60, estimate.Average);
        Assert.Equal(FpsLimiter.Engine, estimate.Limiter);
    }

    [Fact]
    public void ThisPcInCs2At1080pIsLimitedByTheGpu()
    {
        var estimate = FpsEstimator.Estimate(Game("Counter-Strike 2"), 36, 77, FullHdHigh);

        Assert.Equal(122, estimate.Average);
        Assert.Equal(FpsLimiter.Gpu, estimate.Limiter);
        Assert.Equal(FpsRating.Great, estimate.Rating);
    }

    [Fact]
    public void ResolutionPresetAndUpscalingMoveOnlyTheGpuSide()
    {
        var game = Game("Cyberpunk 2077");
        var fullHd = FpsEstimator.Estimate(game, 100, 1000, FullHdHigh).Average;
        var ultraHd = FpsEstimator.Estimate(game, 100, 1000, FullHdHigh with { Resolution = EstimateResolution.UltraHd }).Average;
        var upscaled = FpsEstimator.Estimate(game, 100, 1000, new EstimateSettings(EstimateResolution.UltraHd, EstimatePreset.High, EstimateUpscaling.Performance)).Average;
        var low = FpsEstimator.Estimate(game, 100, 1000, FullHdHigh with { Preset = EstimatePreset.Low }).Average;

        Assert.Equal(95, fullHd);
        Assert.Equal(Math.Round(95 * 0.42), ultraHd);
        Assert.Equal(Math.Round(95 * 0.42 * 1.75), upscaled);
        Assert.Equal(Math.Round(95 * 1.6), low);
    }

    [Theory]
    [InlineData(144, FpsRating.Excellent)]
    [InlineData(143, FpsRating.Great)]
    [InlineData(90, FpsRating.Great)]
    [InlineData(60, FpsRating.Playable)]
    [InlineData(30, FpsRating.Weak)]
    [InlineData(29, FpsRating.Unplayable)]
    public void RatingsFollowTheUsualBands(double fps, FpsRating expected) =>
        Assert.Equal(expected, new FpsEstimate(fps, fps * 0.75, FpsLimiter.Gpu).Rating);

    [Fact]
    public void ZeroOrNegativeIndexesAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FpsEstimator.Estimate(Game("Valorant"), 0, 100, FullHdHigh));
        Assert.Throws<ArgumentOutOfRangeException>(() => FpsEstimator.Estimate(Game("Valorant"), 100, -1, FullHdHigh));
    }

    [Fact]
    public void TheCatalogHasNoDuplicatesAndOnlyPositiveReferences()
    {
        Assert.Equal(FpsEstimator.Games.Count, FpsEstimator.Games.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(FpsEstimator.Games, g => Assert.True(g.GpuReference > 0 && g.CpuCap > 0 && g.EngineCap >= 0));
    }
}

public sealed class HowManyFpsClientTests : IDisposable
{
    private static readonly ReferenceQuery Query = new("Ryzen 7 5700X", "RX 5500 XT", "Counter-Strike 2", EstimateResolution.FullHd, EstimateUpscaling.Off);

    private readonly TempFolder _temp = new();
    private readonly MemoryGameSettings _settings = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 20, 0, 0, TimeSpan.FromHours(-3)));
    private readonly ScriptedHandler _handler = new();

    public void Dispose() => _temp.Dispose();

    private HowManyFpsClient Create() => new(_handler, _settings, _temp.At("fps-cache.json"), _time, NullLogger<HowManyFpsClient>.Instance);

    [Fact]
    public async Task WithoutAKeyNothingIsSentAndTheCallerIsTold()
    {
        using var client = Create();

        var error = await Assert.ThrowsAsync<ReferenceFpsException>(() => client.GetAsync(Query, TestContext.Current.CancellationToken));

        Assert.False(client.IsConfigured);
        Assert.Equal(ReferenceFailure.NotConfigured, error.Failure);
        Assert.Equal(0, _handler.Calls);
    }

    [Fact]
    public async Task TheKeyIsStoredEncryptedAndSentWithTheQuery()
    {
        FpsApiKey.Save(_settings, "  chave-secreta  ");
        _handler.Reply = (HttpStatusCode.OK, """{"avgFps": 131.4, "min1Fps": 92.1}""");
        using var client = Create();

        var result = await client.GetAsync(Query, TestContext.Current.CancellationToken);

        Assert.True(client.IsConfigured);
        Assert.DoesNotContain("chave-secreta", _settings.Read(s => s.ProtectedFpsApiKey), StringComparison.Ordinal);
        Assert.Equal("chave-secreta", FpsApiKey.Read(_settings, NullLogger.Instance));
        Assert.Equal(new ReferenceFps(131.4, 92.1, false), result);
        Assert.Equal("Bearer chave-secreta", _handler.Authorization);
        using var body = JsonDocument.Parse(_handler.Body!);
        Assert.Equal("Counter-Strike 2", body.RootElement.GetProperty("game").GetString());
        Assert.Equal("1920x1080", body.RootElement.GetProperty("resolution").GetString());
        Assert.Equal("Off", body.RootElement.GetProperty("upscaling").GetString());
        Assert.Equal(HowManyFpsClient.Endpoint, _handler.Uri);
    }

    [Fact]
    public async Task AnswersAreCachedOnDiskForSevenDays()
    {
        FpsApiKey.Save(_settings, "k");
        _handler.Reply = (HttpStatusCode.OK, """{"avgFps": 100, "min1Fps": 70}""");
        using (var first = Create())
        {
            await first.GetAsync(Query, TestContext.Current.CancellationToken);
        }

        using var second = Create();
        var cached = await second.GetAsync(Query, TestContext.Current.CancellationToken);
        _time.Advance(HowManyFpsClient.CacheLifetime + TimeSpan.FromMinutes(1));
        var fresh = await second.GetAsync(Query, TestContext.Current.CancellationToken);

        Assert.True(cached.FromCache);
        Assert.False(fresh.FromCache);
        Assert.Equal(2, _handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ReferenceFailure.KeyRefused)]
    [InlineData(HttpStatusCode.Forbidden, ReferenceFailure.KeyRefused)]
    [InlineData(HttpStatusCode.NotFound, ReferenceFailure.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, ReferenceFailure.QuotaExhausted)]
    [InlineData(HttpStatusCode.BadGateway, ReferenceFailure.ServerError)]
    public async Task ErrorAnswersBecomeAKnownFailure(HttpStatusCode status, ReferenceFailure expected)
    {
        FpsApiKey.Save(_settings, "k");
        _handler.Reply = (status, "{}");
        using var client = Create();

        var error = await Assert.ThrowsAsync<ReferenceFpsException>(() => client.GetAsync(Query, TestContext.Current.CancellationToken));

        Assert.Equal(expected, error.Failure);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{"min1Fps": 50}""")]
    [InlineData("""{"avgFps": -3}""")]
    [InlineData("""{"avgFps": 99999999}""")]
    public async Task AnAnswerWithoutAUsableAverageIsRejectedAndNotCached(string json)
    {
        FpsApiKey.Save(_settings, "k");
        _handler.Reply = (HttpStatusCode.OK, json);
        using var client = Create();

        var error = await Assert.ThrowsAsync<ReferenceFpsException>(() => client.GetAsync(Query, TestContext.Current.CancellationToken));

        Assert.Equal(ReferenceFailure.BadResponse, error.Failure);
        Assert.False(File.Exists(_temp.At("fps-cache.json")));
    }

    [Fact]
    public async Task ALowAboveTheAverageIsDroppedAndANetworkErrorIsReported()
    {
        FpsApiKey.Save(_settings, "k");
        _handler.Reply = (HttpStatusCode.OK, """{"avgFps": 80, "min1Fps": 120}""");
        using var client = Create();

        var result = await client.GetAsync(Query, TestContext.Current.CancellationToken);
        _handler.Throw = new HttpRequestException("Nenhuma conexão pôde ser feita");
        var error = await Assert.ThrowsAsync<ReferenceFpsException>(() => client.GetAsync(Query with { Game = "Valorant" }, TestContext.Current.CancellationToken));

        Assert.Equal(0, result.Low1);
        Assert.Equal(ReferenceFailure.Network, error.Failure);
    }

    [Fact]
    public void AKeyCopiedFromAnotherUserCannotBeReadAndIsTreatedAsMissing()
    {
        _settings.Update(s => s.ProtectedFpsApiKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("não é DPAPI")));

        Assert.Null(FpsApiKey.Read(_settings, NullLogger.Instance));
        FpsApiKey.Save(_settings, "   ");
        Assert.Null(_settings.Read(s => s.ProtectedFpsApiKey));
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Json) Reply { get; set; } = (HttpStatusCode.OK, "{}");

        public Exception? Throw { get; set; }

        public int Calls { get; private set; }

        public string? Authorization { get; private set; }

        public string? Body { get; private set; }

        public Uri? Uri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw is not null)
            {
                throw Throw;
            }

            Uri = request.RequestUri;
            Authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Reply.Status) { Content = new StringContent(Reply.Json, Encoding.UTF8, "application/json") };
        }
    }
}
