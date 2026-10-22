using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Privacy;
using M1vClock.Core.Results;
using M1vClock.Hardware;
using M1vClock.Hardware.Network;

namespace M1vClock.App.Tests;

public sealed class NetworkRowsTests
{
    private static NetworkAdapter Wifi() => new(
        "Wi-Fi", "Intel Wi-Fi 6 AX200", NetworkAdapterKind.Wifi, 866_700_000, "192.168.0.15", "192.168.0.1", ["192.168.0.1", "1.1.1.1"], new SensitiveText("AA-BB-CC-DD-EE-FF"));

    private static PingStats Ping(PingRole role, string address, double average, double loss = 0, double jitter = 0.8) =>
        new(role, address, 30, (int)(30 - loss * 30 / 100), average - 1, average, average + 6, jitter);

    private static Dictionary<string, string> Values(IEnumerable<InfoRowViewModel> rows) => rows.ToDictionary(r => r.Label, r => r.Value);

    [Fact]
    public void TheActiveAdapterIsDescribedInPlainWords()
    {
        var values = Values(NetworkRows.Adapter(Wifi()));

        Assert.Equal("Wi-Fi", values["Adaptador"]);
        Assert.Equal("Wi-Fi", values["Tipo"]);
        Assert.Equal("867 Mbps", values["Velocidade do link"]);
        Assert.Equal("192.168.0.15", values["Endereço IP"]);
        Assert.Equal("192.168.0.1", values["Roteador (gateway)"]);
        Assert.Equal("192.168.0.1, 1.1.1.1", values["Servidores DNS"]);
    }

    [Fact]
    public void TheMacAddressIsASensitiveRowThatStaysMaskedUntilRevealed()
    {
        var mac = NetworkRows.Adapter(Wifi()).Single(r => r.Label == "Endereço MAC");

        Assert.True(mac.IsSensitive);
        Assert.Equal(SensitiveText.Mask, mac.Display);
        mac.ToggleRevealCommand.Execute(null);
        Assert.Equal("AA-BB-CC-DD-EE-FF", mac.Display);
    }

    [Fact]
    public void MissingNumbersShowADash()
    {
        var bare = Wifi() with { LinkBitsPerSecond = 0, Ipv4 = null, Gateway = null, DnsServers = [] };

        var values = Values(NetworkRows.Adapter(bare));

        Assert.Equal("—", values["Velocidade do link"]);
        Assert.Equal("—", values["Endereço IP"]);
        Assert.Equal("—", values["Roteador (gateway)"]);
        Assert.Equal("—", values["Servidores DNS"]);
    }

    [Fact]
    public void OtherAdaptersAreOneLineEach()
    {
        var cable = Wifi() with { Name = "Ethernet", Kind = NetworkAdapterKind.Ethernet, LinkBitsPerSecond = 1_000_000_000, Ipv4 = "192.168.0.20" };

        var row = Assert.Single(NetworkRows.Others([cable]));

        Assert.Equal("Ethernet", row.Label);
        Assert.Equal("Ethernet (cabo) · 1.000 Mbps · 192.168.0.20", row.Value);
    }

    [Theory]
    [InlineData(NetworkVerdict.Stable, "Estável", MetricState.Good)]
    [InlineData(NetworkVerdict.Warning, "Atenção", MetricState.Warning)]
    [InlineData(NetworkVerdict.Bad, "Ruim", MetricState.Critical)]
    public void VerdictsHaveATextAndATone(NetworkVerdict verdict, string text, MetricState tone)
    {
        Assert.Equal(text, NetworkRows.Verdict(verdict));
        Assert.Equal(tone, NetworkRows.Tone(verdict));
    }

    [Fact]
    public void FindingsCarryTheirNumbersWithTheRightPrecision()
    {
        Assert.Equal(
            "A rede local está instável (roteador: 45 ms, perda 3,5%, jitter 18 ms). Se for Wi-Fi, aproxime-se do roteador ou use cabo.",
            NetworkRows.Finding(new NetworkFinding(NetworkFindingKind.RouterUnstable, 44.6, 3.5, 18.2)));
        Assert.Equal("Download baixo (4,5 Mbps) para jogos, streaming e atualizações.", NetworkRows.Finding(new NetworkFinding(NetworkFindingKind.LowDownload, 4.5)));
        Assert.StartsWith("A latência sobe para 180 ms", NetworkRows.Finding(new NetworkFinding(NetworkFindingKind.Bufferbloat, 180.4)), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFindingKindHasAText()
    {
        foreach (var kind in Enum.GetValues<NetworkFindingKind>())
        {
            Assert.False(NetworkRows.Finding(new NetworkFinding(kind, 1, 2, 3)).StartsWith('['), $"falta o texto de {kind}");
        }
    }

    [Fact]
    public void ACleanRunGetsTheReassuringNoteInsteadOfAnEmptyList()
    {
        var notes = NetworkRows.Notes(new NetworkAssessment(NetworkVerdict.Stable, []));

        Assert.StartsWith("Latência baixa, sem perda de pacotes", Assert.Single(notes), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnsweringHostShowsAverageMinMaxJitterAndLoss()
    {
        var text = NetworkRows.PingText(Ping(PingRole.InternetPrimary, "1.1.1.1", 12, loss: 3.3333333, jitter: 1.25));

        Assert.Equal("média 12 ms · mín 11 · máx 18 · jitter 1,3 ms · perda 3,3%", text);
    }

    [Fact]
    public void ASilentHostSaysItDoesNotAnswer()
    {
        var silent = new PingStats(PingRole.Router, "192.168.0.1", 30, 0, 0, 0, 0, 0);

        Assert.Equal("Não responde", NetworkRows.PingText(silent));
    }

    [Fact]
    public void MetricsListThePingsThenDnsSpeedAndLoadedLatency()
    {
        var measurements = new NetworkMeasurements(
            Ping(PingRole.Router, "192.168.0.1", 2),
            [Ping(PingRole.InternetPrimary, "1.1.1.1", 12), Ping(PingRole.InternetSecondary, "1.0.0.1", 13)],
            DnsMilliseconds: 24.4,
            DownloadMbps: 287.46,
            UploadMbps: null,
            LoadedLatencyMs: 61.2,
            SpeedTestFailed: false);

        var rows = NetworkRows.Metrics(measurements);

        Assert.Equal(
            ["Roteador (192.168.0.1)", "Internet · Cloudflare (1.1.1.1)", "Internet · Cloudflare 2 (1.0.0.1)", "Resolução de DNS", "Download", "Upload", "Latência sob carga"],
            rows.Select(r => r.Label));
        var values = Values(rows);
        Assert.Equal("24 ms", values["Resolução de DNS"]);
        Assert.Equal("287,5 Mbps", values["Download"]);
        Assert.Equal("—", values["Upload"]);
        Assert.Equal("61 ms (durante o download)", values["Latência sob carga"]);
    }

    [Fact]
    public void WithoutDnsOrLoadedLatencyThoseRowsAreOmitted()
    {
        var measurements = new NetworkMeasurements(Ping(PingRole.Router, "192.168.0.1", 2), [], null, 100, 20, null, false);

        Assert.Equal(["Roteador (192.168.0.1)", "Download", "Upload"], NetworkRows.Metrics(measurements).Select(r => r.Label));
    }

    [Fact]
    public void EveryStageHasAText()
    {
        foreach (var stage in Enum.GetValues<NetworkStage>())
        {
            Assert.False(NetworkRows.Stage(stage).StartsWith('['), $"falta o texto de {stage}");
        }
    }
}

public sealed class NetworkPageViewModelTests
{
    private static NetworkAdapter Ethernet() => new(
        "Ethernet", "Realtek Gaming 2.5GbE", NetworkAdapterKind.Ethernet, 1_000_000_000, "192.168.0.10", "192.168.0.1", ["1.1.1.1"], new SensitiveText("11-22-33-44-55-66"));

    private static NetworkPageViewModel Create(StubNetwork network) =>
        new(network, new NetworkDiagnosticViewModel(network, new CollectingLogger<NetworkDiagnosticViewModel>()), new CollectingLogger<NetworkPageViewModel>());

    [Fact]
    public async Task TheActiveAdapterFillsTheFirstCard()
    {
        var page = Create(new StubNetwork { Overview = ReadResult.Success(new NetworkOverview(Ethernet(), [])) });

        await page.RefreshAsync();

        var card = Assert.Single(page.Sections);
        Assert.Equal("Conexão ativa", card.Title);
        Assert.Equal("Realtek Gaming 2.5GbE", card.Subtitle);
        Assert.True(card.IsReady);
        Assert.False(card.HasStatus);
        Assert.Equal(7, card.Rows.Count);
    }

    [Fact]
    public async Task OtherAdaptersGetTheirOwnCardOnlyWhenThereAreSome()
    {
        var wifi = Ethernet() with { Name = "Wi-Fi", Kind = NetworkAdapterKind.Wifi };
        var page = Create(new StubNetwork { Overview = ReadResult.Success(new NetworkOverview(Ethernet(), [wifi])) });

        await page.RefreshAsync();

        Assert.Equal(["Conexão ativa", "Outros adaptadores"], page.Sections.Select(s => s.Title));
        Assert.Equal("Wi-Fi", page.Sections[1].Rows[0].Label);
    }

    [Fact]
    public async Task WithoutAConnectionTheCardSaysSoAndExplainsWhatToCheck()
    {
        var page = Create(new StubNetwork { Overview = ReadResult.Success(new NetworkOverview(null, [])) });

        await page.RefreshAsync();

        var card = Assert.Single(page.Sections);
        Assert.Equal("Sem rede", card.StatusText);
        Assert.Equal(MetricState.Critical, card.StatusTone);
        Assert.Empty(card.Rows);
        Assert.StartsWith("O Windows não encontrou uma conexão ativa", Assert.Single(card.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedReadShowsTheErrorOnTheCardWithRetry()
    {
        var page = Create(new StubNetwork { Overview = ReadResult.Failure<NetworkOverview>("Sem acesso aos adaptadores.") });

        await page.RefreshAsync();

        var card = Assert.Single(page.Sections);
        Assert.True(card.IsFailed);
        Assert.Equal("Sem acesso aos adaptadores.", card.Error);
        Assert.Same(page.RefreshCommand, card.RetryCommand);
    }

    [Fact]
    public async Task RefreshingDoesNotDuplicateTheCardsOrLoseTheConnectionState()
    {
        var network = new StubNetwork { Overview = ReadResult.Success(new NetworkOverview(null, [])) };
        var page = Create(network);
        await page.RefreshAsync();

        network.Overview = ReadResult.Success(new NetworkOverview(Ethernet(), []));
        await page.RefreshAsync();

        var card = Assert.Single(page.Sections);
        Assert.False(card.HasStatus);
        Assert.Empty(card.Notes);
        Assert.Equal(7, card.Rows.Count);
    }

    [Fact]
    public async Task LeavingThePageCancelsARunningTest()
    {
        var network = new StubNetwork { Overview = ReadResult.Success(new NetworkOverview(Ethernet(), [])), HoldTest = true };
        var page = Create(network);
        var running = page.Diagnostic.ToggleAsync();
        await network.TestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        page.OnNavigatedFrom();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(page.Diagnostic.IsRunning);
        Assert.Equal("Teste cancelado.", page.Diagnostic.Message);
    }
}

public sealed class NetworkDiagnosticViewModelTests
{
    private static NetworkAdapter Ethernet() => new(
        "Ethernet", "Realtek", NetworkAdapterKind.Ethernet, 1_000_000_000, "192.168.0.10", "192.168.0.1", ["1.1.1.1"], new SensitiveText("11-22-33-44-55-66"));

    private static NetworkTestReport Report(NetworkVerdict verdict, params NetworkFinding[] findings)
    {
        var router = new PingStats(PingRole.Router, "192.168.0.1", 30, 30, 1, 2, 4, 0.5);
        var internet = new PingStats(PingRole.InternetPrimary, "1.1.1.1", 30, 30, 10, 12, 20, 1);
        var measurements = new NetworkMeasurements(router, [internet], 20, 300, 90, 30, false);
        return new NetworkTestReport(Ethernet(), measurements, new NetworkAssessment(verdict, findings), new DateTimeOffset(2026, 10, 21, 15, 30, 0, TimeSpan.Zero));
    }

    private static NetworkDiagnosticViewModel Create(StubNetwork network, CollectingLogger<NetworkDiagnosticViewModel>? logger = null) =>
        new(network, logger ?? new CollectingLogger<NetworkDiagnosticViewModel>());

    [Fact]
    public void BeforeAnyTestThereIsNoResultAndTheButtonStartsIt()
    {
        var diagnostic = Create(new StubNetwork());

        Assert.False(diagnostic.IsRunning);
        Assert.False(diagnostic.HasResult);
        Assert.False(diagnostic.HasMessage);
        Assert.Equal("Testar a rede", diagnostic.ButtonText);
    }

    [Fact]
    public async Task ACompletedTestShowsTheVerdictTheMetricsAndTheFindings()
    {
        var finding = new NetworkFinding(NetworkFindingKind.HighJitter, 35);
        var diagnostic = Create(new StubNetwork { Report = Report(NetworkVerdict.Warning, finding) });

        await diagnostic.ToggleAsync();

        Assert.True(diagnostic.HasResult);
        Assert.False(diagnostic.IsRunning);
        Assert.Equal(1, diagnostic.Progress);
        Assert.Equal("Atenção", diagnostic.Result.StatusText);
        Assert.Equal(MetricState.Warning, diagnostic.Result.StatusTone);
        Assert.Equal(NetworkRows.Finding(finding), Assert.Single(diagnostic.Result.Notes));
        Assert.Equal(6, diagnostic.Result.Rows.Count);
        Assert.StartsWith("Testado às", diagnostic.Result.Subtitle, StringComparison.Ordinal);
        Assert.Equal("Testar a rede", diagnostic.ButtonText);
    }

    [Fact]
    public async Task AnotherRunClearsThePreviousResultWhileItWorks()
    {
        var network = new StubNetwork { Report = Report(NetworkVerdict.Stable) };
        var diagnostic = Create(network);
        await diagnostic.ToggleAsync();
        network.HoldTest = true;

        var second = diagnostic.ToggleAsync();
        await network.TestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(diagnostic.IsRunning);
        Assert.False(diagnostic.HasResult);
        Assert.Equal("Cancelar", diagnostic.ButtonText);
        diagnostic.Cancel();
        await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProgressFollowsTheStagesOfTheTest()
    {
        var network = new StubNetwork { Report = Report(NetworkVerdict.Stable), HoldTest = true };
        var diagnostic = Create(network);
        var running = diagnostic.ToggleAsync();
        await network.TestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        network.Progress!.Report(new NetworkProgress(NetworkStage.Download, 0.36));

        Assert.Equal(0.36, diagnostic.Progress);
        Assert.Equal("Medindo a velocidade de download (cerca de 8 s)…", diagnostic.StageText);
        network.Release();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, diagnostic.Progress);
        Assert.Equal(string.Empty, diagnostic.StageText);
    }

    [Fact]
    public async Task PressingTheButtonWhileRunningCancelsTheTest()
    {
        var network = new StubNetwork { HoldTest = true };
        var diagnostic = Create(network);
        var running = diagnostic.ToggleAsync();
        await network.TestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await diagnostic.ToggleAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(diagnostic.IsRunning);
        Assert.False(diagnostic.HasResult);
        Assert.Equal("Teste cancelado.", diagnostic.Message);
        Assert.Equal(0, diagnostic.Progress);
    }

    [Fact]
    public async Task TheCommandStaysEnabledWhileTheTestRunsSoTheButtonCanCancel()
    {
        var network = new StubNetwork { HoldTest = true };
        var diagnostic = Create(network);

        diagnostic.ToggleCommand.Execute(null);
        await network.TestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(diagnostic.IsRunning);
        Assert.True(diagnostic.ToggleCommand.CanExecute(null));
        diagnostic.ToggleCommand.Execute(null);
        await WaitUntilAsync(() => !diagnostic.IsRunning);
        Assert.Equal("Teste cancelado.", diagnostic.Message);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task WithoutAConnectionTheMessageExplainsWhatToCheck()
    {
        var diagnostic = Create(new StubNetwork { Report = null });

        await diagnostic.ToggleAsync();

        Assert.False(diagnostic.HasResult);
        Assert.StartsWith("O Windows não encontrou uma conexão ativa", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnexpectedFailureGivesAShortMessageAndGoesToTheLog()
    {
        var logger = new CollectingLogger<NetworkDiagnosticViewModel>();
        var diagnostic = Create(new StubNetwork { Failure = new HardwareReadException("O endereço do roteador não é um IPv4 válido.") }, logger);

        await diagnostic.ToggleAsync();

        Assert.Equal("O teste não terminou. O registro tem os detalhes.", diagnostic.Message);
        Assert.False(diagnostic.IsRunning);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(2017, entry.EventId.Id);
        Assert.IsType<HardwareReadException>(entry.Exception);
    }
}

internal sealed class StubNetwork : INetworkService
{
    private readonly TaskCompletionSource _release = new();

    public ReadResult<NetworkOverview> Overview { get; set; } = ReadResult.Success(new NetworkOverview(null, []));

    public NetworkTestReport? Report { get; set; }

    public Exception? Failure { get; set; }

    public bool HoldTest { get; set; }

    public IProgress<NetworkProgress>? Progress { get; private set; }

    public TaskCompletionSource TestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _release.TrySetResult();

    public Task<ReadResult<NetworkOverview>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Overview);

    public async Task<NetworkTestReport?> RunTestAsync(IProgress<NetworkProgress>? progress, CancellationToken cancellationToken)
    {
        Progress = progress;
        TestStarted.TrySetResult();
        if (Failure is not null)
        {
            throw Failure;
        }

        if (HoldTest)
        {
            await _release.Task.WaitAsync(cancellationToken);
        }

        return Report;
    }
}
