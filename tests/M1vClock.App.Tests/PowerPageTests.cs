using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Pages.Hardware;
using M1vClock.Core.Results;
using M1vClock.Hardware.Power;

namespace M1vClock.App.Tests;

public sealed class PowerRowsTests
{
    private static BatteryInfo Laptop() => new(
        0, "SMP", "DELL 4GVMP", BatteryChemistry.LithiumIon, BatteryState.Discharging, 72, TimeSpan.FromMinutes(190),
        56.0, 41.23, 224, 11.4, null, 9.5, false, false, true);

    private static Dictionary<string, string> Values(IReadOnlyList<InfoRowViewModel> rows) => rows.ToDictionary(r => r.Label, r => r.Value);

    [Fact]
    public void BatteryMetricsAreFormattedForHumans()
    {
        var values = Values(PowerRows.Metrics(Laptop(), onMains: false));

        Assert.Equal("72%", values["Carga atual"]);
        Assert.Equal("Descarregando", values["Estado"]);
        Assert.Equal("3 h 10 min", values["Autonomia estimada"]);
        Assert.Equal("56,0 Wh", values["Capacidade de projeto"]);
        Assert.Equal("41,2 Wh", values["Capacidade máxima atual"]);
        Assert.Equal("26,4%", values["Desgaste"]);
        Assert.Equal("224", values["Ciclos de carga"]);
        Assert.Equal("11,40 V", values["Tensão"]);
        Assert.Equal("9,5 W", values["Consumo atual"]);
        Assert.Equal("SMP", values["Fabricante"]);
        Assert.Equal("Íon-lítio", values["Química"]);
        Assert.DoesNotContain("Potência de carga", values.Keys);
    }

    [Fact]
    public void TheRuntimeIsHiddenWhileOnTheMains()
    {
        var values = Values(PowerRows.Metrics(Laptop() with { Discharging = false, Charging = true, ChargeWatts = 30 }, onMains: true));

        Assert.DoesNotContain("Autonomia estimada", values.Keys);
        Assert.Equal("30,0 W", values["Potência de carga"]);
        Assert.DoesNotContain("Consumo atual", values.Keys);
    }

    [Fact]
    public void WhatTheBatteryDoesNotReportIsSimplyOmitted()
    {
        var bare = Laptop() with { ChargePercent = null, EstimatedRuntime = null, DesignWattHours = null, FullWattHours = null, Cycles = null, Volts = null, Manufacturer = null, Model = null, Chemistry = BatteryChemistry.Unknown };

        var rows = PowerRows.Metrics(bare, onMains: false);

        Assert.Equal(["Estado", "Consumo atual"], rows.Select(r => r.Label));
    }

    [Fact]
    public void ThePowerStateFallsBackToTheChargingFlags()
    {
        Assert.Equal("Carregando", PowerRows.StateText(Laptop() with { State = BatteryState.Unknown, Charging = true, Discharging = false }));
        Assert.Equal("Descarregando", PowerRows.StateText(Laptop() with { State = BatteryState.Unknown, Discharging = true }));
        Assert.Null(PowerRows.StateText(Laptop() with { State = BatteryState.Unknown, Discharging = false }));
    }

    [Theory]
    [InlineData(BatteryVerdict.Good, "Saudável", MetricState.Good)]
    [InlineData(BatteryVerdict.Warning, "Atenção", MetricState.Warning)]
    [InlineData(BatteryVerdict.Worn, "Desgastada", MetricState.Critical)]
    [InlineData(BatteryVerdict.Unknown, "Sem dados", MetricState.Neutral)]
    public void VerdictsHaveATextAndATone(BatteryVerdict verdict, string text, MetricState tone)
    {
        Assert.Equal(text, PowerRows.Verdict(verdict));
        Assert.Equal(tone, PowerRows.Tone(verdict));
    }

    [Fact]
    public void FindingsCarryTheirNumbers()
    {
        Assert.Equal("Perdeu 52% da capacidade original: a autonomia é metade do que era. Vale trocar a bateria.", PowerRows.Finding(new BatteryFinding(BatteryFindingKind.WornOut, 52)));
        Assert.Equal("1.024 ciclos de carga: perto do fim da vida útil típica das baterias de íon-lítio (500 a 1000).", PowerRows.Finding(new BatteryFinding(BatteryFindingKind.ManyCycles, 1024)));
        Assert.Equal("Carga baixa e fora da tomada.", PowerRows.Finding(new BatteryFinding(BatteryFindingKind.LowCharge, 10)));
    }

    [Fact]
    public void EveryFindingKindHasAText()
    {
        foreach (var kind in Enum.GetValues<BatteryFindingKind>())
        {
            Assert.False(PowerRows.Finding(new BatteryFinding(kind, 1)).StartsWith('['), $"falta o texto de {kind}");
        }
    }

    [Fact]
    public void TitleNumbersTheBatteriesOnlyWhenThereIsMoreThanOne()
    {
        Assert.Equal("Bateria", PowerRows.Title(Laptop(), 1));
        Assert.Equal("Bateria 2", PowerRows.Title(Laptop() with { Index = 1 }, 2));
        Assert.Equal("SMP · 72% de carga", PowerRows.Subtitle(Laptop()));
    }

    [Fact]
    public void EnergyShowsTheSourceAndTheCustomPlanMarkedAsCustom()
    {
        var plan = new PowerPlan(Guid.NewGuid(), "Atlas Power Scheme", PowerPlanKind.Custom);

        var values = Values(PowerRows.Energy(PowerSource.DesktopMains, plan));

        Assert.Equal("Tomada (PC de mesa)", values["Fonte de energia"]);
        Assert.Equal("Atlas Power Scheme (personalizado)", values["Plano de energia"]);
    }

    [Fact]
    public void ADesktopGetsTheNoBatteryAndPsuNotes()
    {
        var plan = new PowerPlan(Guid.NewGuid(), "Custom", PowerPlanKind.Custom);

        var notes = PowerRows.EnergyNotes(PowerSource.DesktopMains, plan);

        Assert.Equal(2, notes.Count);
        Assert.Equal("Nenhuma bateria detectada neste PC.", notes[0]);
        Assert.StartsWith("O Windows não mede o consumo da fonte", notes[1], StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyKnownSlowPlansGetTheHighPerformanceAdvice()
    {
        var balanced = new PowerPlan(PowerPlanParser.Balanced, "Equilibrado", PowerPlanKind.Balanced);
        var high = new PowerPlan(PowerPlanParser.HighPerformance, "Alto desempenho", PowerPlanKind.HighPerformance);
        var custom = new PowerPlan(Guid.NewGuid(), "Meu plano", PowerPlanKind.Custom);

        Assert.Contains(PowerRows.EnergyNotes(PowerSource.Mains, balanced), n => n.StartsWith("Para jogos", StringComparison.Ordinal));
        Assert.DoesNotContain(PowerRows.EnergyNotes(PowerSource.Mains, high), n => n.StartsWith("Para jogos", StringComparison.Ordinal));
        Assert.DoesNotContain(PowerRows.EnergyNotes(PowerSource.Mains, custom), n => n.StartsWith("Para jogos", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnreadablePlanIsSaidSo()
    {
        var notes = PowerRows.EnergyNotes(PowerSource.Mains, null);

        Assert.Equal(["Não foi possível identificar o plano de energia ativo."], notes);
        Assert.Equal(["Fonte de energia"], PowerRows.Energy(PowerSource.Mains, null).Select(r => r.Label));
    }
}

public sealed class PowerPageViewModelTests
{
    private static PowerReport Desktop() => new(
        ReadResult.Success<IReadOnlyList<BatteryReport>>([]),
        PowerSource.DesktopMains,
        ReadResult.Success(new PowerPlan(Guid.NewGuid(), "Atlas Power Scheme", PowerPlanKind.Custom)));

    private static BatteryInfo Worn() => new(0, "SMP", "X", BatteryChemistry.LithiumIon, BatteryState.Discharging, 15, null, 50.0, 20.0, 900, null, null, null, false, false, true);

    private static PowerPageViewModel Create(Func<CancellationToken, Task<PowerReport>> read) =>
        new(new StubPower(read), new CollectingLogger<PowerPageViewModel>());

    [Fact]
    public async Task ADesktopShowsOnlyTheEnergyCardWithItsNotes()
    {
        var page = Create(_ => Task.FromResult(Desktop()));

        await page.RefreshAsync();

        var energy = Assert.Single(page.Sections);
        Assert.Equal("Energia", energy.Title);
        Assert.True(energy.IsReady);
        Assert.Equal(2, energy.Notes.Count);
        Assert.Equal(["Tomada (PC de mesa)", "Atlas Power Scheme (personalizado)"], energy.Rows.Select(r => r.Value));
    }

    [Fact]
    public async Task ALaptopShowsTheBatteryCardBeforeTheEnergyCard()
    {
        var battery = Worn();
        var report = new PowerReport(
            ReadResult.Success<IReadOnlyList<BatteryReport>>([new BatteryReport(battery, BatteryEvaluator.Evaluate(battery, onMains: false))]),
            PowerSource.Battery,
            ReadResult.Success(new PowerPlan(PowerPlanParser.Balanced, "Equilibrado", PowerPlanKind.Balanced)));
        var page = Create(_ => Task.FromResult(report));

        await page.RefreshAsync();

        Assert.Equal(["Bateria", "Energia"], page.Sections.Select(s => s.Title));
        var card = page.Sections[0];
        Assert.Equal("Desgastada", card.StatusText);
        Assert.Equal(MetricState.Critical, card.StatusTone);
        Assert.Equal(3, card.Notes.Count);
        Assert.Contains("Para jogos", string.Concat(page.Sections[1].Notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedBatteryReadStillShowsTheEnergyCard()
    {
        var report = Desktop() with { Batteries = ReadResult.Failure<IReadOnlyList<BatteryReport>>("Sem WMI.") };
        var page = Create(_ => Task.FromResult(report));

        await page.RefreshAsync();

        Assert.Equal(2, page.Sections.Count);
        Assert.True(page.Sections[0].IsFailed);
        Assert.Equal("Sem WMI.", page.Sections[0].Error);
        Assert.True(page.Sections[1].IsReady);
    }

    [Fact]
    public async Task RefreshingKeepsASingleEnergyCard()
    {
        var page = Create(_ => Task.FromResult(Desktop()));

        await page.RefreshAsync();
        await page.RefreshAsync();

        Assert.Single(page.Sections);
        Assert.Same(page.RefreshCommand, page.Sections[0].RetryCommand);
    }

    private sealed class StubPower(Func<CancellationToken, Task<PowerReport>> read) : IPowerService
    {
        public Task<PowerReport> ReadAsync(CancellationToken cancellationToken) => read(cancellationToken);
    }
}
