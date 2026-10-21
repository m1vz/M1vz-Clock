using M1vClock.App.Formatting;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Results;
using M1vClock.Hardware.Gpu;

namespace M1vClock.App.Tests;

public sealed class GpuRowsTests
{
    private const ulong Mebibyte = 1024UL * 1024;
    private static readonly GpuSpecDatabase Db = GpuSpecDatabase.Default;

    private static GpuInfo Radeon() => new(
        "AMD Radeon RX 5500 XT",
        "AMD",
        PciId.Parse(@"PCI\VEN_1002&DEV_7340&SUBSYS_0B0C1002&REV_C5\6&26A46C08&0&00000019"),
        Db.Find("1002", "7340", "AMD Radeon RX 5500 XT"),
        8151 * Mebibyte,
        8138 * Mebibyte,
        "32.0.21045.16010",
        new DateOnly(2026, 10, 16),
        DriverSignature.Whql,
        "Microsoft Windows Hardware Compatibility Publisher",
        "12_1",
        [GpuApi.OpenCl, GpuApi.Vulkan, GpuApi.DirectCompute, GpuApi.DirectMl],
        Db.FeaturesOf("RDNA 1"));

    private static GpuInfo Unknown() => new("Placa Misteriosa", "—", null, null, 0, 0, "1.0", null, DriverSignature.Unknown, null, null, [], null);

    private static Dictionary<string, string> Values(IReadOnlyList<InfoRowViewModel> rows) => rows.ToDictionary(r => r.Label, r => r.Value);

    [Fact]
    public void IdentificationDescribesTheRadeon()
    {
        var values = Values(GpuRows.Identification(Radeon(), Db));

        Assert.Equal("AMD Radeon RX 5500 XT", values["Nome"]);
        Assert.Equal("Navi 14", values["Chip"]);
        Assert.Equal("RDNA 1", values["Arquitetura"]);
        Assert.Equal("C5", values["Revisão"]);
        Assert.Equal("AMD", values["Fabricante da placa"]);
        Assert.Equal("1002 7340 · 1002 0B0C", values["ID do dispositivo PCI"]);
        Assert.Equal("TSMC 7 nm", values["Processo de fabricação"]);
        Assert.Equal("158 mm²", values["Área do chip"]);
        Assert.Equal("6,4 bilhões", values["Transistores"]);
        Assert.Equal("12/12/2019", values["Lançamento"]);
    }

    [Fact]
    public void AnUnknownCardShowsDashesInsteadOfInventingData()
    {
        var rows = GpuRows.Identification(Unknown(), Db);

        Assert.Equal("Placa Misteriosa", rows[0].Value);
        Assert.All(rows.Skip(2), r => Assert.Equal(Units.Missing, r.Value));
    }

    [Fact]
    public void ProcessingUnitsUseAmdNamesAndTheoreticalNumbers()
    {
        var values = Values(GpuRows.ProcessingUnits(Radeon()));

        Assert.Equal("1.408", values["Stream processors"]);
        Assert.Equal("22", values["Compute units"]);
        Assert.Equal("32 / 88", values["ROPs / TMUs"]);
        Assert.Equal("59,0 GPixel/s", values["Taxa de pixels"]);
        Assert.Equal("162,4 GTexel/s", values["Taxa de texturas"]);
        Assert.Equal("5,20 TFLOPS", values["FP32 (teórico)"]);
        Assert.DoesNotContain("Ray accelerators", values.Keys);
    }

    [Fact]
    public void RdnaTwoAndNewerAddRayAccelerators()
    {
        var rdna2 = Radeon() with { Spec = Db.Find("1002", "73DF", "RX 6700 XT") };

        var values = Values(GpuRows.ProcessingUnits(rdna2));

        Assert.Equal("40", values["Ray accelerators"]);
    }

    [Fact]
    public void NvidiaCardsUseCudaNamesAndOneSmPerHundredTwentyEightCores()
    {
        var rtx = Radeon() with { Pci = PciId.Parse(@"PCI\VEN_10DE&DEV_2684&SUBSYS_88A41043&REV_A1"), Spec = Db.Find("10DE", "2684", "GeForce RTX 4090") };

        var values = Values(GpuRows.ProcessingUnits(rtx));

        Assert.Equal("16.384", values["CUDA cores"]);
        Assert.Equal("128", values["SMs"]);
    }

    [Fact]
    public void ACardWithoutASpecSaysThereIsNoData()
    {
        var rows = GpuRows.ProcessingUnits(Unknown());

        var row = Assert.Single(rows);
        Assert.Equal("Sem dados", row.Label);
        Assert.Equal("Esta placa não está no banco de especificações.", row.Value);
        Assert.Equal("Sem dados", Assert.Single(GpuRows.Clocks(Unknown())).Label);
    }

    [Fact]
    public void MemoryShowsTheMarketedSizeAndTheUsableAmount()
    {
        var values = Values(GpuRows.Memory(Radeon()));

        Assert.Equal("8 GB (8.151 MB utilizáveis)", values["Tamanho"]);
        Assert.Equal("7,95 GB", values["Compartilhada do sistema"]);
        Assert.Equal("GDDR6", values["Tipo"]);
    }

    [Fact]
    public void IntegratedGraphicsShowTheReservedMemoryInstead()
    {
        var igpu = Radeon() with { Spec = Db.Find("1002", "164E", "AMD Radeon(TM) Graphics"), VideoMemoryBytes = 512 * Mebibyte };

        var values = Values(GpuRows.Memory(igpu));

        Assert.Equal("512 MB", values["Reservada (dedicada)"]);
        Assert.StartsWith("Memória do sistema", values["Tipo"], StringComparison.Ordinal);
    }

    [Fact]
    public void ClocksAreMarkedAsReferenceValues()
    {
        var rows = GpuRows.Clocks(Radeon());

        Assert.Equal(["Base (referência)", "Boost (referência)", "TDP (referência)"], rows.Select(r => r.Label));
        Assert.Equal(["1607 MHz", "1845 MHz", "130 W"], rows.Select(r => r.Value));
    }

    [Fact]
    public void ATdpOfZeroIsLeftOut()
    {
        var igpu = Radeon() with { Spec = Db.Find("1002", "164E", "AMD Radeon(TM) Graphics") };

        Assert.DoesNotContain(GpuRows.Clocks(igpu), r => r.Label.StartsWith("TDP", StringComparison.Ordinal));
    }

    [Fact]
    public void DriverRowsShowVersionDateAndSignature()
    {
        var values = Values(GpuRows.Driver(Radeon()));

        Assert.Equal("32.0.21045.16010", values["Versão do driver"]);
        Assert.Equal("16/10/2026", values["Data do driver"]);
        Assert.Equal("WHQL (Microsoft)", values["Assinatura digital"]);
    }

    [Theory]
    [InlineData(DriverSignature.Whql, "WHQL (Microsoft)")]
    [InlineData(DriverSignature.VendorSigned, "Assinado · AMD Inc.")]
    [InlineData(DriverSignature.NotSigned, "Não assinado")]
    [InlineData(DriverSignature.Unknown, "—")]
    public void EverySignatureHasAText(DriverSignature signature, string expected)
    {
        Assert.Equal(expected, GpuRows.SignatureText(Radeon() with { Signature = signature, Signer = "AMD Inc." }));
    }

    [Theory]
    [InlineData("12_2", "DirectX 12 Ultimate (12_2)")]
    [InlineData("12_1", "DirectX 12 (12.1)")]
    [InlineData("11_0", "DirectX 11 (11.0)")]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    public void DirectXLevelsAreNamedForHumans(string? level, string expected)
    {
        Assert.Equal(expected, GpuRows.DirectXText(level));
    }

    [Fact]
    public void TechnologiesListTheApisAndTheArchitectureFeatures()
    {
        var values = Values(GpuRows.Technologies(Radeon()));

        Assert.Equal("DirectX 12 (12.1)", values["DirectX"]);
        Assert.Equal("OpenCL · Vulkan · DirectCompute · DirectML", values["APIs"]);
        Assert.Contains("Radeon Anti-Lag", values["Recursos da arquitetura"], StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutApisOrFeaturesTheRowsAreHonest()
    {
        var rows = GpuRows.Technologies(Unknown());

        Assert.Equal(["—", "—"], rows.Select(r => r.Value));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void SubtitleSummarizesChipArchitectureProcessMemoryAndBoard()
    {
        Assert.Equal("Navi 14 · RDNA 1 · TSMC 7 nm · 8 GB GDDR6 · AMD", GpuRows.Subtitle(Radeon(), Db));
        Assert.Equal(string.Empty, GpuRows.Subtitle(Unknown(), Db));
    }
}

public sealed class GpuPageViewModelTests
{
    private const ulong Mebibyte = 1024UL * 1024;
    private static readonly GpuSpecDatabase Db = GpuSpecDatabase.Default;

    private static GpuInfo Gpu(string name, string pnp, ulong vram) => new(
        name, "AMD", PciId.Parse(pnp), PciId.Parse(pnp) is { } p ? Db.Find(p.VendorId, p.DeviceId, name) : null,
        vram, 0, "32.0", new DateOnly(2026, 10, 16), DriverSignature.Whql, null, "12_1", [], null);

    private static GpuInfo Radeon() => Gpu("AMD Radeon RX 5500 XT", @"PCI\VEN_1002&DEV_7340&SUBSYS_0B0C1002&REV_C5\1", 8151 * Mebibyte);

    private static GpuPageViewModel Create(Func<CancellationToken, Task<GpuReport>> read) =>
        new(new StubGpu(read), Db, new CollectingLogger<GpuPageViewModel>());

    [Fact]
    public async Task ShowsSixCardsForASingleGpuAndTheSubtitleOnTheFirst()
    {
        var page = Create(_ => Task.FromResult(new GpuReport(ReadResult.Success<IReadOnlyList<GpuInfo>>([Radeon()]))));

        await page.RefreshAsync();

        Assert.Equal(["Identificação", "Unidades de processamento", "Memória de vídeo", "Clocks e energia", "Driver", "Tecnologias"], page.Sections.Select(s => s.Title));
        Assert.All(page.Sections, s => Assert.True(s.IsReady));
        Assert.Equal("Navi 14 · RDNA 1 · TSMC 7 nm · 8 GB GDDR6 · AMD", page.Sections[0].Subtitle);
        Assert.Equal(string.Empty, page.Sections[1].Subtitle);
    }

    [Fact]
    public async Task SeveralGpusGetTheirNameOnEveryCard()
    {
        var igpu = Gpu("AMD Radeon(TM) Graphics", @"PCI\VEN_1002&DEV_164E&SUBSYS_88A41043&REV_C1\2", 512 * Mebibyte);
        var page = Create(_ => Task.FromResult(new GpuReport(ReadResult.Success<IReadOnlyList<GpuInfo>>([Radeon(), igpu]))));

        await page.RefreshAsync();

        Assert.Equal(12, page.Sections.Count);
        Assert.Equal("AMD Radeon RX 5500 XT", page.Sections[0].Subtitle);
        Assert.Equal("AMD Radeon(TM) Graphics", page.Sections[6].Subtitle);
        Assert.All(page.Sections, s => Assert.NotEmpty(s.Subtitle));
    }

    [Fact]
    public async Task ACardOutsideTheDatabaseGetsAnExplanatoryNote()
    {
        var odd = Gpu("Placa Misteriosa", @"PCI\VEN_10DE&DEV_FFFF&SUBSYS_00001043&REV_01\3", 4096 * Mebibyte);
        var page = Create(_ => Task.FromResult(new GpuReport(ReadResult.Success<IReadOnlyList<GpuInfo>>([odd]))));

        await page.RefreshAsync();

        Assert.Equal(["Esta placa não está no banco de especificações."], page.Sections[0].Notes);
    }

    [Fact]
    public async Task AFailedReadShowsASingleFailedCardWithRetry()
    {
        var page = Create(_ => Task.FromResult(new GpuReport(ReadResult.Failure<IReadOnlyList<GpuInfo>>("Sem placa."))));

        await page.RefreshAsync();

        var failed = Assert.Single(page.Sections);
        Assert.True(failed.IsFailed);
        Assert.Equal("Sem placa.", failed.Error);
        Assert.Same(page.RefreshCommand, failed.RetryCommand);
    }

    [Fact]
    public async Task RefreshingReplacesTheCards()
    {
        var page = Create(_ => Task.FromResult(new GpuReport(ReadResult.Success<IReadOnlyList<GpuInfo>>([Radeon()]))));

        await page.RefreshAsync();
        await page.RefreshAsync();

        Assert.Equal(6, page.Sections.Count);
        Assert.StartsWith("Chip, unidades, clocks", page.FooterNote, StringComparison.Ordinal);
    }

    private sealed class StubGpu(Func<CancellationToken, Task<GpuReport>> read) : IGpuService
    {
        public Task<GpuReport> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
