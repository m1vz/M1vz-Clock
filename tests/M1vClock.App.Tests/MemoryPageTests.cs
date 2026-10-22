using M1vClock.App.Formatting;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Privacy;
using M1vClock.Core.Results;
using M1vClock.Hardware.Ram;

namespace M1vClock.App.Tests;

public sealed class RamRowsTests
{
    private const ulong Gigabyte = 1024UL * 1024 * 1024;

    private static RamModule Module(string locator = "DIMM_A2", int configured = 3200) =>
        new("BANK 1", locator, 8 * Gigabyte, RamType.Ddr4, RamFormFactor.Dimm, 3200, configured, "Kingston", "KF3200C16D4/8GX", new SensitiveText("0A1B2C3D"), 64, 64, 1, 1.2, 1.2, 1.2);

    private static RamSummary Summary(params RamModule[] modules) => RamAnalyzer.Summarize(modules, new RamBoard(4, 128 * Gigabyte));

    [Fact]
    public void SummaryDescribesThisMachine()
    {
        var rows = RamRows.Summary(Summary(Module(), Module("DIMM_B2")));
        var values = rows.ToDictionary(r => r.Label, r => r.Value);

        Assert.Equal("16 GB", values["Total instalado"]);
        Assert.Equal("2 de 4 slots", values["Módulos instalados"]);
        Assert.Equal("128 GB", values["Capacidade máxima da placa"]);
        Assert.Equal("DDR4", values["Tipo"]);
        Assert.Equal("3200 MT/s", values["Velocidade em uso"]);
        Assert.Equal("3200 MT/s", values["Velocidade nominal"]);
        Assert.Equal("2 canais (dual channel ou mais)", values["Canais (estimado)"]);
        Assert.Equal("Não", values["ECC"]);
        Assert.Equal("1,20 V", values["Tensão em uso"]);
        Assert.StartsWith("Provavelmente ativo: roda a 3200 MT/s", values["Perfil EXPO/XMP (estimado)"], StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileOffAsksToTurnOnExpoInTheBios()
    {
        var text = RamRows.ProfileText(RamAnalyzer.EstimateProfile([Module(configured: 2133)]));

        Assert.Contains("desligado", text, StringComparison.Ordinal);
        Assert.Contains("2133", text, StringComparison.Ordinal);
        Assert.Contains("3200", text, StringComparison.Ordinal);
        Assert.Contains("EXPO/XMP", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProfileVerdict.SpeedUnreadable, "Não foi possível ler")]
    [InlineData(ProfileVerdict.BelowRated, "abaixo da velocidade nominal")]
    [InlineData(ProfileVerdict.CannotEstimate, "Sem como estimar")]
    public void EveryProfileVerdictHasAnExplanation(ProfileVerdict verdict, string fragment)
    {
        var text = RamRows.ProfileText(new ProfileEstimate(verdict, 2666, 0, 3200));

        Assert.Contains(fragment, text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryChannelVerdictHasAnExplanation()
    {
        Assert.Contains("Canal único (1 módulo)", RamRows.ChannelText(RamAnalyzer.EstimateChannels([Module()])), StringComparison.Ordinal);
        Assert.Contains("mesmo canal", RamRows.ChannelText(RamAnalyzer.EstimateChannels([Module("DIMM_A1"), Module("DIMM_A2")])), StringComparison.Ordinal);
        var unknown = RamRows.ChannelText(RamAnalyzer.EstimateChannels([Module("DIMM 0"), Module("DIMM 1")]));
        Assert.Contains("DIMM 0, DIMM 1", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleModuleSummaryOmitsTheVoltageWhenUnknown()
    {
        var bare = Module() with { ConfiguredVolts = null };

        var rows = RamRows.Summary(Summary(bare));

        Assert.DoesNotContain(rows, r => r.Label == "Tensão em uso");
    }

    [Fact]
    public void ModuleRowsKeepTheSerialHidden()
    {
        var rows = RamRows.Module(Module());

        var serial = rows[^1];
        Assert.True(serial.IsSensitive);
        Assert.Equal(SensitiveText.Mask, serial.Display);
        Assert.Equal("8 GB", rows[0].Value);
        Assert.Equal("DDR4", rows[1].Value);
        Assert.Equal("DIMM", rows[2].Value);
        Assert.Equal("1 (simples)", rows[3].Value);
        Assert.Equal("1,20 V a 1,20 V", rows[7].Value);
        Assert.Equal("64 / 64 bits", rows[8].Value);
        Assert.Equal("KF3200C16D4/8GX", rows[10].Value);
    }

    [Fact]
    public void UnknownModuleFieldsShowADash()
    {
        var module = Module() with { Ranks = 0, ConfiguredVolts = null, MinVolts = null, MaxVolts = null, DataWidth = 0, Manufacturer = string.Empty, ConfiguredSpeed = 0 };

        var rows = RamRows.Module(module);

        Assert.Equal(Units.Missing, rows[3].Value);
        Assert.Equal(Units.Missing, rows[4].Value);
        Assert.Equal(Units.Missing, rows[6].Value);
        Assert.Equal(Units.Missing, rows[7].Value);
        Assert.Equal(Units.Missing, rows[8].Value);
        Assert.Equal(Units.Missing, rows[9].Value);
    }

    [Fact]
    public void ModuleTitleNamesTheSlotAndTheSize()
    {
        Assert.Equal("DIMM_A2 · 8 GB", RamRows.ModuleTitle(Module()));
        Assert.Equal("BANK 1 · 8 GB", RamRows.ModuleTitle(Module(locator: string.Empty)));
    }

    [Fact]
    public void UsageShowsPhysicalAndVirtualWithPercentages()
    {
        var usage = new RamUsage(16_660_976UL * 1024, 4_165_548UL * 1024, 44_972_528UL * 1024, 26_836_544UL * 1024);

        var rows = RamRows.Usage(usage);

        Assert.Equal("Memória física em uso", rows[0].Label);
        Assert.Equal("11,92 GB de 15,89 GB (75%)", rows[0].Value);
        Assert.Equal("17,3 GB de 42,89 GB (40%)", rows[1].Value);
    }

    [Theory]
    [InlineData(RamType.Ddr5, "DDR5")]
    [InlineData(RamType.LpDdr5, "LPDDR5")]
    [InlineData(RamType.Unknown, "Desconhecido")]
    public void TypeNamesAreReadable(RamType type, string expected)
    {
        Assert.Equal(expected, RamRows.TypeText(type));
    }

    [Theory]
    [InlineData(RamFormFactor.SoDimm, "SO-DIMM")]
    [InlineData(RamFormFactor.Proprietary, "Proprietário")]
    [InlineData(RamFormFactor.Unknown, "—")]
    public void FormFactorNamesAreReadable(RamFormFactor form, string expected)
    {
        Assert.Equal(expected, RamRows.FormText(form));
    }
}

public sealed class MemoryPageViewModelTests
{
    private const ulong Gigabyte = 1024UL * 1024 * 1024;

    private static RamModule Module(string locator) =>
        new("BANK 1", locator, 8 * Gigabyte, RamType.Ddr4, RamFormFactor.Dimm, 3200, 3200, "Kingston", "KF3200C16D4/8GX", new SensitiveText("S"), 64, 64, 1, 1.2, 1.2, 1.2);

    private static readonly RamReport Healthy = new(
        ReadResult.Success(new RamInventory(
            RamAnalyzer.Summarize([Module("DIMM_A2"), Module("DIMM_B2")], new RamBoard(4, 128 * Gigabyte)),
            [Module("DIMM_A2"), Module("DIMM_B2")])),
        ReadResult.Success(new RamUsage(16 * Gigabyte, 4 * Gigabyte, 40 * Gigabyte, 20 * Gigabyte)));

    [Fact]
    public async Task ShowsTheSummaryTheUsageAndOneCardPerModule()
    {
        var page = Create(_ => Task.FromResult(Healthy));

        await page.RefreshAsync();

        Assert.Equal(4, page.Sections.Count);
        Assert.All(page.Sections, s => Assert.True(s.IsReady, s.Title));
        Assert.Equal(["Resumo", "Uso agora", "DIMM_A2 · 8 GB", "DIMM_B2 · 8 GB"], page.Sections.Select(s => s.Title));
    }

    [Fact]
    public async Task AFailedInventoryKeepsTheUsageAndShowsNoModules()
    {
        var report = Healthy with { Inventory = ReadResult.Failure<RamInventory>("Sem módulos.") };
        var page = Create(_ => Task.FromResult(report));

        await page.RefreshAsync();

        Assert.Equal(2, page.Sections.Count);
        Assert.True(page.Sections[0].IsFailed);
        Assert.Equal("Sem módulos.", page.Sections[0].Error);
        Assert.True(page.Sections[1].IsReady);
    }

    [Fact]
    public async Task AFailedUsageKeepsTheModules()
    {
        var report = Healthy with { Usage = ReadResult.Failure<RamUsage>("Sem uso.") };
        var page = Create(_ => Task.FromResult(report));

        await page.RefreshAsync();

        Assert.True(page.Sections[0].IsReady);
        Assert.True(page.Sections[1].IsFailed);
        Assert.Equal(4, page.Sections.Count);
    }

    [Fact]
    public async Task RefreshingReplacesTheModuleCards()
    {
        var page = Create(_ => Task.FromResult(Healthy));

        await page.RefreshAsync();
        await page.RefreshAsync();

        Assert.Equal(4, page.Sections.Count);
        Assert.Equal(Loc("Hw.Common.SerialNote"), page.FooterNote);
    }

    private static MemoryPageViewModel Create(Func<CancellationToken, Task<RamReport>> read) =>
        new(new StubRam(read), new CollectingLogger<MemoryPageViewModel>());

    private static string Loc(string key) => M1vClock.App.Localization.Loc.Get(key);

    private sealed class StubRam(Func<CancellationToken, Task<RamReport>> read) : IRamService
    {
        public Task<RamReport> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
