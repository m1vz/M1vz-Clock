using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Privacy;
using M1vClock.Core.Results;
using M1vClock.Hardware.Storage;

namespace M1vClock.App.Tests;

public sealed class DiskRowsTests
{
    private static PhysicalDisk Disk(
        DriveMedia media = DriveMedia.Ssd,
        DriveBus bus = DriveBus.Nvme,
        WindowsHealth health = WindowsHealth.Healthy,
        bool? predicted = null,
        StorageCounters? counters = null,
        string serial = "SN123") =>
        new(1, "NX-512 2280", new SensitiveText(serial), "SN6329", bus, media, 512_105_932_800, health, predicted, counters, ["C:"]);

    private static StorageCounters FullCounters() => new(38, 70, 4, 4321, 5, 2, 3, 0, 812);

    [Fact]
    public void SubtitleJoinsKindBusSizeAndDriveLetters()
    {
        Assert.Equal("SSD · NVMe · 512,1 GB · Unidades: C:", DiskRows.Subtitle(Disk()));
    }

    [Fact]
    public void SubtitleOmitsWhatIsUnknown()
    {
        var disk = Disk(DriveMedia.Unknown, DriveBus.Unknown) with { SizeBytes = 0, Letters = [] };

        Assert.Equal(string.Empty, DiskRows.Subtitle(disk));
    }

    [Fact]
    public void TheTotalDataWrittenAndReadIsShownInTerabytesWhenTheDriveReportsIt()
    {
        var counters = FullCounters() with { BytesWritten = 81_565_000_000_000UL, BytesRead = 62_009_000_000_000UL };

        var values = DiskRows.Metrics(Disk(counters: counters)).ToDictionary(r => r.Label, r => r.Value);

        Assert.Equal("81,6 TB", values["Total gravado"]);
        Assert.Equal("62 TB", values["Total lido"]);
    }

    [Fact]
    public void WithoutTheTotalsTheirRowsAreOmitted()
    {
        var labels = DiskRows.Metrics(Disk(counters: FullCounters())).Select(r => r.Label);

        Assert.DoesNotContain("Total gravado", labels);
        Assert.DoesNotContain("Total lido", labels);
    }

    [Fact]
    public void WithoutCountersOnlyWindowsHealthFirmwareAndSerialAreShown()
    {
        var rows = DiskRows.Metrics(Disk());

        Assert.Equal(["Saúde segundo o Windows", "Firmware", "Número de série"], rows.Select(r => r.Label));
        Assert.True(rows[^1].IsSensitive);
        Assert.Equal(SensitiveText.Mask, rows[^1].Display);
    }

    [Fact]
    public void WithCountersEveryMetricIsFormattedForHumans()
    {
        var rows = DiskRows.Metrics(Disk(predicted: false, counters: FullCounters())).ToDictionary(r => r.Label, r => r.Value);

        Assert.Equal("38 °C (máx. registrado 70)", rows["Temperatura"]);
        Assert.Equal("4%", rows["Desgaste"]);
        Assert.Equal("96%", rows["Vida útil restante"]);
        Assert.Equal("4.321 h (180 dias)", rows["Horas ligado"]);
        Assert.Equal("812", rows["Ciclos liga/desliga"]);
        Assert.Equal("5 (3 não corrigidos)", rows["Erros de leitura"]);
        Assert.Equal("2", rows["Erros de escrita"]);
        Assert.Equal("Não", rows["Falha prevista (S.M.A.R.T.)"]);
        Assert.Equal("Saudável", rows["Saúde segundo o Windows"]);
    }

    [Fact]
    public void APredictedFailureShowsInCapitals()
    {
        var rows = DiskRows.Metrics(Disk(predicted: true));

        Assert.Equal("SIM", rows.Single(r => r.Label == "Falha prevista (S.M.A.R.T.)").Value);
    }

    [Fact]
    public void TemperatureWithoutAMaximumHasNoParenthesis()
    {
        var counters = FullCounters() with { TemperatureMax = null };

        var rows = DiskRows.Metrics(Disk(counters: counters));

        Assert.Equal("38 °C", rows.Single(r => r.Label == "Temperatura").Value);
    }

    [Fact]
    public void WearOverOneHundredNeverShowsNegativeLife()
    {
        var rows = DiskRows.Metrics(Disk(counters: FullCounters() with { Wear = 120 }));

        Assert.Equal("0%", rows.Single(r => r.Label == "Vida útil restante").Value);
    }

    [Theory]
    [InlineData(DiskVerdict.Good, "Saudável", MetricState.Good)]
    [InlineData(DiskVerdict.Warning, "Atenção", MetricState.Warning)]
    [InlineData(DiskVerdict.Critical, "Crítico", MetricState.Critical)]
    [InlineData(DiskVerdict.Unknown, "Sem dados", MetricState.Neutral)]
    public void VerdictsHaveATextAndATone(DiskVerdict verdict, string text, MetricState tone)
    {
        Assert.Equal(text, DiskRows.Verdict(verdict));
        Assert.Equal(tone, DiskRows.Tone(verdict));
    }

    [Fact]
    public void FindingsAreWrittenWithTheirNumbers()
    {
        Assert.Equal("Desgaste de 82%: o SSD já gastou boa parte da vida útil. Planeje a troca.", DiskRows.Finding(new DiskFinding(FindingKind.WearHigh, FindingSeverity.Warning, 82)));
        Assert.Equal("Temperatura de 71 °C está alta. Melhore a ventilação.", DiskRows.Finding(new DiskFinding(FindingKind.TemperatureHigh, FindingSeverity.Warning, 71)));
        Assert.Equal("Erros não corrigidos: 3 de leitura e 1 de escrita.", DiskRows.Finding(new DiskFinding(FindingKind.UncorrectedErrors, FindingSeverity.Warning, 3, 1)));
        Assert.StartsWith("O próprio disco avisa", DiskRows.Finding(new DiskFinding(FindingKind.FailurePredicted, FindingSeverity.Critical)), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFindingKindHasAText()
    {
        foreach (var kind in Enum.GetValues<FindingKind>())
        {
            var text = DiskRows.Finding(new DiskFinding(kind, FindingSeverity.Warning, 1, 1));

            Assert.False(text.StartsWith('['), $"falta o texto de {kind}");
        }
    }

    [Fact]
    public void NotesListCriticalFindingsFirst()
    {
        var assessment = DiskHealthEvaluator.Evaluate(Disk(predicted: true, health: WindowsHealth.Warning));

        var notes = DiskRows.Notes(assessment);

        Assert.StartsWith("O próprio disco avisa", notes[0], StringComparison.Ordinal);
        Assert.Equal(2, notes.Count);
    }

    [Fact]
    public void ADiskWithoutProblemsAndOneWithoutDataGetDifferentSentences()
    {
        var good = DiskRows.Notes(DiskHealthEvaluator.Evaluate(Disk()));
        var unknown = DiskRows.Notes(DiskHealthEvaluator.Evaluate(Disk(health: WindowsHealth.Unknown)));

        Assert.Equal(["Nenhum problema encontrado nas leituras disponíveis."], good);
        Assert.Equal(["Este disco não informa dados de saúde (comum em pendrives e alguns HDs externos)."], unknown);
    }
}

public sealed class VolumeCardViewModelTests
{
    private const ulong Gigabyte = 1024UL * 1024 * 1024;

    private static VolumeCardViewModel Card(ulong total = 100, ulong free = 50, string label = "", string disk = "NX-512 2280") =>
        new(new Volume("C:", label, VolumeKind.Internal, "NTFS", disk, total * Gigabyte, free * Gigabyte));

    [Fact]
    public void TitleShowsTheLetterAndTheLabelWhenThereIsOne()
    {
        Assert.Equal("C:", Card().Title);
        Assert.Equal("C:  wasd", Card(label: "wasd").Title);
    }

    [Fact]
    public void MetaJoinsKindFileSystemAndDisk()
    {
        Assert.Equal("Interno · NTFS · NX-512 2280", Card().Meta);
        Assert.Equal("Interno · NTFS", Card(disk: string.Empty).Meta);
    }

    [Fact]
    public void SizesSayUsedFreeAndTotal()
    {
        Assert.Equal("50 GB usados · 50 GB livres de 100 GB", Card().Sizes);
    }

    [Theory]
    [InlineData(100, 21, MetricState.Neutral)]
    [InlineData(100, 20, MetricState.Warning)]
    [InlineData(100, 11, MetricState.Warning)]
    [InlineData(100, 10, MetricState.Critical)]
    [InlineData(100, 0, MetricState.Critical)]
    public void TheRingTurnsAmberAtEightyAndRedAtNinetyPercent(ulong total, ulong free, MetricState expected)
    {
        Assert.Equal(expected, Card(total, free).State);
    }

    [Fact]
    public void AccessibleNameSaysHowFullTheVolumeIs()
    {
        var card = Card(100, 12);

        Assert.Equal("88%", card.UsedText);
        Assert.Equal("C:: 88% usado", card.AccessibleName);
        Assert.Equal(0.88, card.Fraction, 3);
    }
}

public sealed class StoragePageViewModelTests
{
    private static PhysicalDisk Hdd() => new(0, "ST1000VM002-1SD102", new SensitiveText("Z9"), "SC11", DriveBus.Sata, DriveMedia.Hdd, 1_000_202_273_280, WindowsHealth.Healthy, null, null, ["D:"]);

    private static PhysicalDisk Ssd() => new(1, "NX-512 2280", new SensitiveText("0000"), "SN6329", DriveBus.Nvme, DriveMedia.Ssd, 512_105_932_800, WindowsHealth.Healthy, null, null, ["C:"]);

    private static DiskReport Report(PhysicalDisk disk) => new(disk, DiskHealthEvaluator.Evaluate(disk));

    private static StorageReport Healthy(CounterAccess access = CounterAccess.NeedsAdministrator) => new(
        ReadResult.Success<IReadOnlyList<DiskReport>>([Report(Hdd()), Report(Ssd())]),
        ReadResult.Success<IReadOnlyList<Volume>>([new Volume("C:", string.Empty, VolumeKind.Internal, "NTFS", "NX-512 2280", 508_846_665_728, 59_918_897_152)]),
        access,
        access);

    private static StoragePageViewModel Create(Func<CancellationToken, Task<StorageReport>> read) =>
        new(new StubStorage(read), new CollectingLogger<StoragePageViewModel>());

    [Fact]
    public async Task ShowsOneCardPerDiskAndOneRingPerVolume()
    {
        var page = Create(_ => Task.FromResult(Healthy()));

        await page.RefreshAsync();

        Assert.Equal(["ST1000VM002-1SD102", "NX-512 2280"], page.Sections.Select(s => s.Title));
        Assert.All(page.Sections, s => Assert.True(s.IsReady));
        Assert.All(page.Sections, s => Assert.Equal("Saudável", s.StatusText));
        var volume = Assert.Single(page.Volumes);
        Assert.Equal("C:", volume.Title);
        Assert.True(page.VolumesStatus.IsReady);
    }

    [Fact]
    public async Task WithoutAdministratorTheNoticeExplainsWhatIsMissing()
    {
        var page = Create(_ => Task.FromResult(Healthy(CounterAccess.NeedsAdministrator)));

        await page.RefreshAsync();

        Assert.StartsWith("Temperatura, desgaste, horas ligado", page.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithTheCountersReadThereIsNoNotice()
    {
        var page = Create(_ => Task.FromResult(Healthy(CounterAccess.Read)));

        await page.RefreshAsync();

        Assert.Equal(string.Empty, page.Notice);
    }

    [Fact]
    public async Task ACounterlessWindowsGetsTheUnavailableNotice()
    {
        var page = Create(_ => Task.FromResult(Healthy(CounterAccess.Unavailable)));

        await page.RefreshAsync();

        Assert.StartsWith("O Windows deste PC não expõe", page.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedDisksStillShowTheVolumes()
    {
        var report = Healthy() with { Disks = ReadResult.Failure<IReadOnlyList<DiskReport>>("Sem discos.") };
        var page = Create(_ => Task.FromResult(report));

        await page.RefreshAsync();

        var failed = Assert.Single(page.Sections);
        Assert.True(failed.IsFailed);
        Assert.Equal("Sem discos.", failed.Error);
        Assert.Single(page.Volumes);
        Assert.StartsWith("Temperatura, desgaste", page.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedVolumesStillShowTheDisks()
    {
        var report = Healthy() with { Volumes = ReadResult.Failure<IReadOnlyList<Volume>>("Sem unidades.") };
        var page = Create(_ => Task.FromResult(report));

        await page.RefreshAsync();

        Assert.Empty(page.Volumes);
        Assert.True(page.VolumesStatus.IsFailed);
        Assert.Equal(2, page.Sections.Count);
    }

    [Fact]
    public async Task RefreshingReplacesTheCards()
    {
        var page = Create(_ => Task.FromResult(Healthy()));

        await page.RefreshAsync();
        await page.RefreshAsync();

        Assert.Equal(2, page.Sections.Count);
        Assert.Single(page.Volumes);
    }

    [Fact]
    public async Task ACriticalDiskShowsTheCriticalBadgeAndItsReasons()
    {
        var sick = Ssd() with { Health = WindowsHealth.Unhealthy };
        var report = Healthy() with { Disks = ReadResult.Success<IReadOnlyList<DiskReport>>([Report(sick)]) };
        var page = Create(_ => Task.FromResult(report));

        await page.RefreshAsync();

        var section = Assert.Single(page.Sections);
        Assert.Equal("Crítico", section.StatusText);
        Assert.Equal(MetricState.Critical, section.StatusTone);
        Assert.Equal(["O Windows classifica o disco como não saudável."], section.Notes);
    }

    private sealed class StubStorage(Func<CancellationToken, Task<StorageReport>> read) : IStorageService
    {
        public Task<StorageReport> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
