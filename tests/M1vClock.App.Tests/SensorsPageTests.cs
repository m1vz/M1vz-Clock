using System.Globalization;
using System.IO;
using M1vClock.App.Formatting;
using M1vClock.App.Hosting;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Sensors;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.App.Tests;

internal sealed class FakeSensorService : ISensorService
{
    public SensorServiceState State { get; set; } = SensorServiceState.Stopped;

    public SensorSnapshot? Latest { get; set; }

    public int Subscriptions { get; private set; }

    public int Releases { get; private set; }

    public event EventHandler<SensorSnapshot>? SnapshotReady;

    public event EventHandler? StateChanged;

    public bool HasSnapshotSubscribers => SnapshotReady is not null;

    public IDisposable Subscribe()
    {
        Subscriptions++;
        return new Lease(() => Releases++);
    }

    public IDisposable Pause() => new Lease(() => { });

    public SensorSnapshot? Component { get; set; }

    public SensorSnapshot? ReadComponent(SensorHardware component) => Component;

    public void Publish(SensorSnapshot snapshot)
    {
        Latest = snapshot;
        SnapshotReady?.Invoke(this, snapshot);
    }

    public void Change(SensorServiceState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Lease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}

internal sealed class FakePawnIo(bool installed) : IPawnIoProbe
{
    public bool IsInstalled { get; } = installed;
}

public sealed class SensorFormatTests
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(SensorKind.Temperature, 55.25f, "55,3 °C")]
    [InlineData(SensorKind.Load, 23.46f, "23,5 %")]
    [InlineData(SensorKind.Clock, 4450.4f, "4.450 MHz")]
    [InlineData(SensorKind.Power, 41.55f, "41,6 W")]
    [InlineData(SensorKind.Voltage, 1.2251f, "1,225 V")]
    [InlineData(SensorKind.Current, 0.5f, "0,5 A")]
    [InlineData(SensorKind.Fan, 1250f, "1.250 RPM")]
    [InlineData(SensorKind.Data, 12.345f, "12,35 GB")]
    [InlineData(SensorKind.SmallData, 512.04f, "512 MB")]
    [InlineData(SensorKind.Noise, 31.2f, "31,2 dBA")]
    [InlineData(SensorKind.Factor, 0.987f, "0,99")]
    public void ValuesCarryTheirUnitAndTheCultureSeparators(SensorKind kind, float value, string expected)
    {
        Assert.Equal(expected, SensorFormat.Value(kind, value, TemperatureUnit.Celsius, Pt));
    }

    [Fact]
    public void FahrenheitConvertsTheTemperatureOnly()
    {
        Assert.Equal("131 °F", SensorFormat.Value(SensorKind.Temperature, 55f, TemperatureUnit.Fahrenheit, En));
        Assert.Equal("55 %", SensorFormat.Value(SensorKind.Load, 55f, TemperatureUnit.Fahrenheit, En));
    }

    [Fact]
    public void ThroughputIsShownAsBytesPerSecond()
    {
        Assert.Equal("1,5 MB/s", SensorFormat.Value(SensorKind.Throughput, 1_572_864f, TemperatureUnit.Celsius, Pt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void MissingOrInvalidReadingsShowADash(float? value)
    {
        Assert.Equal("—", SensorFormat.Value(SensorKind.Temperature, value));
    }

    [Fact]
    public void EverySensorKindHasAFormat()
    {
        foreach (var kind in Enum.GetValues<SensorKind>())
        {
            Assert.NotEqual("—", SensorFormat.Value(kind, 1f, TemperatureUnit.Celsius, Pt));
        }
    }
}

public sealed class SensorRowsTests
{
    private static SensorReading Reading(string id, string hardwareName, SensorHardware hardware, string name, SensorKind kind, float? value = 1f) =>
        new(id, hardwareName, hardware, name, kind, value, value, value);

    private static SensorRowViewModel Row(string id, string hardwareName, SensorHardware hardware, string name, SensorKind kind, float? value = 1f) =>
        new(Reading(id, hardwareName, hardware, name, kind, value), TemperatureUnit.Celsius);

    [Fact]
    public void ARowFormatsItsValueMinAndMax()
    {
        var row = new SensorRowViewModel(new SensorReading("a", "CPU", SensorHardware.Cpu, "Core", SensorKind.Temperature, 61f, 40f, 78.5f), TemperatureUnit.Celsius);

        Assert.Equal("61 °C", row.Value);
        Assert.Equal("40 °C", row.Min);
        Assert.Equal("78,5 °C", row.Max);
    }

    [Fact]
    public void UpdatingARowNotifiesOnlyWhatChanged()
    {
        var row = Row("a", "CPU", SensorHardware.Cpu, "Core", SensorKind.Temperature, 61f);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.Update(Reading("a", "CPU", SensorHardware.Cpu, "Core", SensorKind.Temperature, 62f), TemperatureUnit.Celsius);

        Assert.Equal(["Max", "Min", "Value"], changed.Distinct().Order());
        changed.Clear();
        row.Update(Reading("a", "CPU", SensorHardware.Cpu, "Core", SensorKind.Temperature, 62f), TemperatureUnit.Celsius);
        Assert.Empty(changed);
    }

    [Theory]
    [InlineData("core", true)]
    [InlineData("CORE #1", true)]
    [InlineData("ryzen", true)]
    [InlineData("processador", true)]
    [InlineData("core ryzen", true)]
    [InlineData("core geforce", false)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void TheSearchMatchesNameHardwareAndCategoryWithEveryWord(string query, bool expected)
    {
        var row = Row("a", "AMD Ryzen 7 5700X", SensorHardware.Cpu, "Core #1", SensorKind.Clock);

        Assert.Equal(expected, SensorRows.Matches(row, query, SensorFilterKind.All));
    }

    [Theory]
    [InlineData(SensorKind.Temperature, SensorFilterKind.Temperature)]
    [InlineData(SensorKind.Load, SensorFilterKind.Load)]
    [InlineData(SensorKind.Clock, SensorFilterKind.Clock)]
    [InlineData(SensorKind.Power, SensorFilterKind.Power)]
    [InlineData(SensorKind.Fan, SensorFilterKind.Fan)]
    [InlineData(SensorKind.Voltage, SensorFilterKind.Other)]
    [InlineData(SensorKind.Throughput, SensorFilterKind.Other)]
    public void EveryKindBelongsToAFilter(SensorKind kind, SensorFilterKind expected)
    {
        Assert.Equal(expected, SensorRows.FilterOf(kind));
    }

    [Fact]
    public void TheKindFilterHidesTheOtherKinds()
    {
        var temp = Row("a", "CPU", SensorHardware.Cpu, "Core", SensorKind.Temperature);
        var load = Row("b", "CPU", SensorHardware.Cpu, "Core", SensorKind.Load);

        Assert.True(SensorRows.Matches(temp, string.Empty, SensorFilterKind.Temperature));
        Assert.False(SensorRows.Matches(load, string.Empty, SensorFilterKind.Temperature));
        Assert.True(SensorRows.Matches(load, string.Empty, SensorFilterKind.All));
    }

    [Fact]
    public void GroupsFollowTheHardwareOrderAndRowsFollowTheKindOrder()
    {
        var rows = new[]
        {
            Row("1", "Samsung SSD", SensorHardware.Storage, "Temperature", SensorKind.Temperature),
            Row("2", "AMD Radeon", SensorHardware.Gpu, "GPU Core", SensorKind.Load),
            Row("3", "AMD Ryzen", SensorHardware.Cpu, "CPU Total", SensorKind.Load),
            Row("4", "AMD Ryzen", SensorHardware.Cpu, "Core #1", SensorKind.Clock),
            Row("5", "AMD Ryzen", SensorHardware.Cpu, "Core #1", SensorKind.Temperature),
        };

        var groups = SensorRows.Group(rows);

        Assert.Equal(["AMD Ryzen", "AMD Radeon", "Samsung SSD"], groups.Select(g => g.Title));
        Assert.Equal(["Processador", "Placa de vídeo", "Armazenamento"], groups.Select(g => g.Subtitle));
        Assert.Equal(["4", "5", "3"], groups[0].Rows.Select(r => r.Id));
        Assert.Equal("Icon.Cpu", groups[0].IconKey);
    }

    [Fact]
    public void TwoSameKindDevicesStayInSeparateGroups()
    {
        var rows = new[]
        {
            Row("1", "NVMe A", SensorHardware.Storage, "Temperature", SensorKind.Temperature),
            Row("2", "NVMe B", SensorHardware.Storage, "Temperature", SensorKind.Temperature),
        };

        Assert.Equal(["NVMe A", "NVMe B"], SensorRows.Group(rows).Select(g => g.Title));
    }

    [Fact]
    public void EveryHardwareAndFilterHasAnIconAndALabel()
    {
        foreach (var hardware in Enum.GetValues<SensorHardware>())
        {
            Assert.StartsWith("Icon.", SensorRows.IconFor(hardware), StringComparison.Ordinal);
            Assert.False(SensorRows.HardwareLabel(hardware).StartsWith('['), $"falta o texto de {hardware}");
        }

        foreach (var filter in Enum.GetValues<SensorFilterKind>())
        {
            Assert.False(SensorRows.FilterLabel(filter).StartsWith('['), $"falta o texto de {filter}");
        }
    }
}

public sealed class SensorsPageViewModelTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("m1vclock-sensors-tests");
    private readonly FakeSensorService _service = new();
    private readonly AppSettingsService _settings;

    public SensorsPageViewModelTests()
    {
        var store = new SettingsStore<AppSettings>(Path.Combine(_temp.FullName, "settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);
        _settings = new AppSettingsService(store, NullLogger<AppSettingsService>.Instance);
    }

    public void Dispose() => _temp.Delete(recursive: true);

    private static SensorReading R(string id, string hardwareName, SensorHardware hardware, string name, SensorKind kind, float? value) =>
        new(id, hardwareName, hardware, name, kind, value, value, value);

    private static SensorSnapshot Snapshot(params SensorReading[] readings) =>
        new(new DateTimeOffset(2026, 10, 21, 12, 0, 1, TimeSpan.Zero), readings);

    private static SensorSnapshot Healthy() => Snapshot(
        R("cpu/temp", "AMD Ryzen", SensorHardware.Cpu, "Core (Tctl/Tdie)", SensorKind.Temperature, 58f),
        R("cpu/load", "AMD Ryzen", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 12f),
        R("gpu/load", "AMD Radeon", SensorHardware.Gpu, "GPU Core", SensorKind.Load, 30f));

    private SensorsPageViewModel Create(bool elevated = true, bool pawnIo = true) =>
        new(_service, new FakePawnIo(pawnIo), new AppInfo("0.1.0", _temp.FullName, elevated), _settings, new InlineUiThread());

    [Fact]
    public void OpeningThePageSubscribesAndShowsTheWaitingState()
    {
        var page = Create();
        _service.State = SensorServiceState.Starting;

        page.OnNavigatedTo();

        Assert.Equal(1, _service.Subscriptions);
        Assert.True(page.IsWaiting);
        Assert.False(page.IsFailed);
        Assert.Empty(page.Groups);
        Assert.False(page.HasNoMatches);
    }

    [Fact]
    public void AnExistingSnapshotIsShownImmediately()
    {
        _service.State = SensorServiceState.Running;
        _service.Latest = Healthy();
        var page = Create();

        page.OnNavigatedTo();

        Assert.False(page.IsWaiting);
        Assert.Equal(["AMD Ryzen", "AMD Radeon"], page.Groups.Select(g => g.Title));
        Assert.Equal("3 sensores · atualizado às " + new DateTimeOffset(2026, 10, 21, 12, 0, 1, TimeSpan.Zero).LocalDateTime.ToString("T", CultureInfo.GetCultureInfo("pt-BR")), page.Summary);
    }

    [Fact]
    public void ANewSnapshotUpdatesTheValuesWithoutRebuildingTheGroups()
    {
        _service.State = SensorServiceState.Running;
        var page = Create();
        page.OnNavigatedTo();
        _service.Publish(Healthy());
        var group = page.Groups[0];
        var row = group.Rows[0];

        _service.Publish(Snapshot(
            R("cpu/temp", "AMD Ryzen", SensorHardware.Cpu, "Core (Tctl/Tdie)", SensorKind.Temperature, 63f),
            R("cpu/load", "AMD Ryzen", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 15f),
            R("gpu/load", "AMD Radeon", SensorHardware.Gpu, "GPU Core", SensorKind.Load, 31f)));

        Assert.Same(group, page.Groups[0]);
        Assert.Same(row, page.Groups[0].Rows[0]);
        Assert.Equal("63 °C", page.Groups[0].Rows[0].Value);
        Assert.Equal("15 %", page.Groups[0].Rows.Single(r => r.Id == "cpu/load").Value);
    }

    [Fact]
    public void ANewSensorAppearingRebuildsTheGroupsAndAGoneOneLeaves()
    {
        _service.State = SensorServiceState.Running;
        var page = Create();
        page.OnNavigatedTo();
        _service.Publish(Healthy());

        _service.Publish(Snapshot(R("cpu/load", "AMD Ryzen", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 12f), R("nvme/temp", "NVMe", SensorHardware.Storage, "Temperature", SensorKind.Temperature, 40f)));

        Assert.Equal(["AMD Ryzen", "NVMe"], page.Groups.Select(g => g.Title));
        Assert.Single(page.Groups[0].Rows);
    }

    [Fact]
    public void SearchingFiltersTheGroupsAndAnEmptyResultSaysSo()
    {
        _service.State = SensorServiceState.Running;
        _service.Latest = Healthy();
        var page = Create();
        page.OnNavigatedTo();

        page.Query = "radeon";
        Assert.Equal(["AMD Radeon"], page.Groups.Select(g => g.Title));
        Assert.False(page.HasNoMatches);

        page.Query = "nada disso";
        Assert.Empty(page.Groups);
        Assert.True(page.HasNoMatches);

        page.Query = string.Empty;
        Assert.Equal(2, page.Groups.Count);
        Assert.False(page.HasNoMatches);
    }

    [Fact]
    public void TheKindFilterNarrowsTheRows()
    {
        _service.State = SensorServiceState.Running;
        _service.Latest = Healthy();
        var page = Create();
        page.OnNavigatedTo();

        page.FilterIndex = (int)SensorFilterKind.Temperature;

        var group = Assert.Single(page.Groups);
        Assert.Equal("Core (Tctl/Tdie)", Assert.Single(group.Rows).Name);
        Assert.Equal(["Todos", "Temperatura", "Carga", "Clock", "Consumo", "Ventoinhas", "Outros"], page.FilterLabels);
    }

    [Fact]
    public void FreezingKeepsTheValuesAndResumingCatchesUp()
    {
        _service.State = SensorServiceState.Running;
        var page = Create();
        page.OnNavigatedTo();
        _service.Publish(Healthy());

        page.ToggleFreezeCommand.Execute(null);
        _service.Publish(Snapshot(R("cpu/load", "AMD Ryzen", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 99f)));

        Assert.True(page.IsFrozen);
        Assert.Equal("Retomar", page.FreezeText);
        Assert.StartsWith("3 sensores · valores congelados às", page.Summary, StringComparison.Ordinal);
        Assert.Equal("12 %", page.Groups[0].Rows.Single(r => r.Id == "cpu/load").Value);

        page.ToggleFreezeCommand.Execute(null);

        Assert.Equal("Congelar", page.FreezeText);
        Assert.Equal("99 %", page.Groups[0].Rows.Single(r => r.Id == "cpu/load").Value);
    }

    [Fact]
    public void TheTemperatureUnitSettingIsUsed()
    {
        _settings.Update(s => s.TemperatureUnit = TemperatureUnit.Fahrenheit);
        _service.State = SensorServiceState.Running;
        _service.Latest = Healthy();
        var page = Create();

        page.OnNavigatedTo();

        Assert.Equal("136,4 °F", page.Groups[0].Rows.Single(r => r.Id == "cpu/temp").Value);
    }

    [Fact]
    public void WhenTheServiceFailsTheErrorIsShown()
    {
        var page = Create();
        page.OnNavigatedTo();

        _service.Change(SensorServiceState.Failed);

        Assert.True(page.IsFailed);
        Assert.False(page.IsWaiting);
        Assert.False(page.HasNoMatches);
    }

    [Fact]
    public void WithoutACpuTemperatureAndWithoutAdministratorTheNoticeAsksForAdministrator()
    {
        _service.State = SensorServiceState.Running;
        _service.Latest = Snapshot(R("cpu/load", "AMD Ryzen", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 12f));
        var page = Create(elevated: false, pawnIo: false);

        page.OnNavigatedTo();

        Assert.True(page.HasNotice);
        Assert.StartsWith("A temperatura e o consumo da CPU não aparecem: o Windows só libera", page.Notice, StringComparison.Ordinal);
        Assert.False(page.HasNoticeCommand);
    }

    [Fact]
    public void ElevatedWithoutPawnIoTheNoticeShowsTheInstallCommand()
    {
        _service.State = SensorServiceState.Running;
        _service.Latest = Snapshot(R("cpu/load", "AMD Ryzen", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 12f));
        var page = Create(elevated: true, pawnIo: false);

        page.OnNavigatedTo();

        Assert.Contains("PawnIO", page.Notice, StringComparison.Ordinal);
        Assert.Equal("winget install --id namazso.PawnIO -e", page.NoticeCommand);
        Assert.True(page.HasNoticeCommand);
    }

    [Fact]
    public void ElevatedWithPawnIoAndNoReadingSaysTheReadFailed()
    {
        _service.State = SensorServiceState.Running;
        _service.Latest = Snapshot(R("cpu/load", "AMD Ryzen", SensorHardware.Cpu, "CPU Total", SensorKind.Load, 12f));
        var page = Create(elevated: true, pawnIo: true);

        page.OnNavigatedTo();

        Assert.StartsWith("O PawnIO está instalado, mas a leitura da CPU falhou", page.Notice, StringComparison.Ordinal);
        Assert.False(page.HasNoticeCommand);
    }

    [Fact]
    public void NoNoticeWhenTheCpuTemperatureIsThere()
    {
        _service.State = SensorServiceState.Running;
        _service.Latest = Healthy();
        var page = Create(elevated: false, pawnIo: false);

        page.OnNavigatedTo();

        Assert.False(page.HasNotice);
    }

    [Fact]
    public void LeavingThePageReleasesTheSubscriptionAndStopsListening()
    {
        var page = Create();
        page.OnNavigatedTo();

        page.OnNavigatedFrom();

        Assert.Equal(1, _service.Releases);
        Assert.False(_service.HasSnapshotSubscribers);
    }

    [Fact]
    public void ReopeningThePageSubscribesAgain()
    {
        var page = Create();
        page.OnNavigatedTo();
        page.OnNavigatedFrom();

        page.OnNavigatedTo();

        Assert.Equal(2, _service.Subscriptions);
    }

    [Fact]
    public void DisposingIsTheSameAsLeaving()
    {
        var page = Create();
        page.OnNavigatedTo();

        page.Dispose();

        Assert.Equal(1, _service.Releases);
    }
}
