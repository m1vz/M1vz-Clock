using System.IO;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Pages.Hardware;
using M1vClock.App.Services;
using M1vClock.Core.Results;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Devices;
using Microsoft.Extensions.Logging.Abstractions;

namespace M1vClock.App.Tests;

internal sealed class StubUsb : IUsbDeviceService
{
    public ReadResult<IReadOnlyList<UsbDeviceInfo>> Result { get; set; } = ReadResult.Success<IReadOnlyList<UsbDeviceInfo>>([]);

    public int Reads { get; private set; }

    public Task<ReadResult<IReadOnlyList<UsbDeviceInfo>>> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(Result);
    }
}

public sealed class DeviceRowsTests
{
    private static UsbDeviceInfo Mouse() =>
        new("USB\\VID_046D&PID_C52B\\A", "Logitech G Pro", "HIDClass", UsbCategory.Input, "046D", "C52B", "Logitech", null, null);

    private static UsbDeviceInfo Board() =>
        new("USB\\VID_2341&PID_8036\\B", "Arduino Leonardo (COM5)", "Ports", UsbCategory.SerialPort, "2341", "8036", "Arduino", "COM5", null);

    [Fact]
    public void TheDetailJoinsCategoryMakerPortAndHardwareId()
    {
        Assert.Equal("Entrada (teclado, mouse, controle) · Logitech · 046D:C52B", DeviceRows.Detail(Mouse()));
        Assert.Equal("Porta serial · Arduino · COM5 · 2341:8036", DeviceRows.Detail(Board()));
    }

    [Fact]
    public void MissingPartsAreSimplyLeftOut()
    {
        var bare = Mouse() with { Maker = null, Vid = null, Pid = null };

        Assert.Equal("Entrada (teclado, mouse, controle)", DeviceRows.Detail(bare));
    }

    [Theory]
    [InlineData(10u, "O dispositivo não inicia (código 10).")]
    [InlineData(28u, "Falta o driver (código 28).")]
    [InlineData(43u, "O dispositivo avisou o Windows de um problema (código 43). Muitas vezes é o cabo ou a porta USB.")]
    [InlineData(99u, "O Windows reportou o código de erro 99.")]
    public void ProblemCodesAreExplainedInPlainWords(uint code, string expected)
    {
        Assert.Equal(expected, DeviceRows.ProblemText(code));
    }

    [Fact]
    public void EveryCategoryHasAText()
    {
        foreach (var category in Enum.GetValues<UsbCategory>())
        {
            Assert.False(DeviceRows.CategoryText(category).StartsWith('['), $"falta o texto de {category}");
        }
    }

    [Fact]
    public void DevicesWithAProblemGoToTheirOwnListInsteadOfTheConnectedOne()
    {
        var broken = Board() with { ProblemCode = 28 };

        var connected = DeviceRows.Connected([Mouse(), broken]);
        var problems = DeviceRows.WithProblem([Mouse(), broken]);

        Assert.Equal(["Logitech G Pro"], connected.Select(r => r.Label));
        var row = Assert.Single(problems);
        Assert.Equal("Arduino Leonardo (COM5)", row.Label);
        Assert.Equal("Falta o driver (código 28).", row.Value);
    }

    [Fact]
    public void ActivityRowsShowTheTimeThenWhatHappened()
    {
        var at = new DateTimeOffset(2026, 10, 21, 15, 30, 5, TimeSpan.Zero);

        var row = Assert.Single(DeviceRows.Activity([new ActivityEntry(at, "Icon.Devices", "Dispositivo USB conectado", "Headset")]));

        Assert.Equal(at.LocalDateTime.ToString("T", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")), row.Label);
        Assert.Equal("Dispositivo USB conectado: Headset", row.Value);
    }
}

public sealed class DevicesPageViewModelTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("m1vclock-devices-tests");
    private readonly StubUsb _usb = new();
    private readonly FakeDeviceWatcher _watcher = new();
    private readonly ActivityLog _activity = new(new InlineUiThread());
    private readonly AppSettingsService _settings;
    private int _writes;

    public DevicesPageViewModelTests()
    {
        var store = new SettingsStore<AppSettings>(Path.Combine(_temp.FullName, "settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);
        _settings = new AppSettingsService(store, NullLogger<AppSettingsService>.Instance);
        _settings.Changed += (_, _) => _writes++;
    }

    public void Dispose() => _temp.Delete(recursive: true);

    private static UsbDeviceInfo Mouse() =>
        new("USB\\VID_046D&PID_C52B\\A", "Logitech G Pro", "HIDClass", UsbCategory.Input, "046D", "C52B", "Logitech", null, null);

    private DevicesPageViewModel Create() => new(_usb, _watcher, _activity, _settings, new CollectingLogger<DevicesPageViewModel>());

    [Fact]
    public async Task ConnectedDevicesAndTheEmptyActivityAreShown()
    {
        _usb.Result = ReadResult.Success<IReadOnlyList<UsbDeviceInfo>>([Mouse()]);
        var page = Create();

        await page.RefreshAsync();

        Assert.Equal(["Dispositivos USB conectados", "Atividade recente"], page.Sections.Select(s => s.Title));
        Assert.Equal("Logitech G Pro", Assert.Single(page.Sections[0].Rows).Label);
        Assert.Empty(page.Sections[0].Notes);
        Assert.StartsWith("Nada foi conectado", Assert.Single(page.Sections[1].Notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoDevicesSaysSoInsteadOfShowingAnEmptyCard()
    {
        var page = Create();

        await page.RefreshAsync();

        Assert.Empty(page.Sections[0].Rows);
        Assert.StartsWith("Nenhum dispositivo USB conectado", Assert.Single(page.Sections[0].Notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeviceWithAProblemGetsAWarningCardAtTheTop()
    {
        var broken = Mouse() with { ProblemCode = 43 };
        _usb.Result = ReadResult.Success<IReadOnlyList<UsbDeviceInfo>>([broken]);
        var page = Create();

        await page.RefreshAsync();

        Assert.Equal(["Dispositivos com problema", "Dispositivos USB conectados", "Atividade recente"], page.Sections.Select(s => s.Title));
        var card = page.Sections[0];
        Assert.Equal("Atenção", card.StatusText);
        Assert.Equal(MetricState.Warning, card.StatusTone);
        Assert.StartsWith("O Windows marcou estes dispositivos com erro", Assert.Single(card.Notes), StringComparison.Ordinal);
        Assert.Empty(page.Sections[1].Rows);
    }

    [Fact]
    public async Task AFailedReadShowsTheErrorWithRetryButKeepsTheActivity()
    {
        _usb.Result = ReadResult.Failure<IReadOnlyList<UsbDeviceInfo>>("Sem acesso ao WMI.");
        _activity.Add(new ActivityEntry(DateTimeOffset.UtcNow, "Icon.Devices", "Dispositivo USB conectado", "Headset"));
        var page = Create();

        await page.RefreshAsync();

        Assert.True(page.Sections[0].IsFailed);
        Assert.Equal("Sem acesso ao WMI.", page.Sections[0].Error);
        Assert.Same(page.RefreshCommand, page.Sections[0].RetryCommand);
        Assert.Single(page.Sections[1].Rows);
    }

    [Fact]
    public async Task RefreshingDoesNotDuplicateTheCards()
    {
        var page = Create();

        await page.RefreshAsync();
        await page.RefreshAsync();

        Assert.Equal(2, page.Sections.Count);
    }

    [Fact]
    public void TheAlertsSwitchReadsAndSavesTheSetting()
    {
        var page = Create();
        Assert.True(page.NotifyDevices);

        page.NotifyDevices = false;

        Assert.False(_settings.Current.NotifyDevices);
        Assert.False(page.NotifyDevices);
        Assert.Equal(1, _writes);
    }

    [Fact]
    public void SettingTheSameValueDoesNotWriteTheSettings()
    {
        var page = Create();

        page.NotifyDevices = true;

        Assert.Equal(0, _writes);
    }

    [Fact]
    public async Task AChangeWhileThePageIsOpenRefreshesTheListAndTheActivity()
    {
        var page = Create();
        page.OnNavigatedTo();
        await WaitUntilAsync(() => _usb.Reads >= 1);
        _usb.Result = ReadResult.Success<IReadOnlyList<UsbDeviceInfo>>([Mouse()]);

        _activity.Add(new ActivityEntry(DateTimeOffset.UtcNow, "Icon.Devices", "Dispositivo USB conectado", "Logitech G Pro"));
        await WaitUntilAsync(() => _usb.Reads >= 2 && page.Sections.Any(s => s.Rows.Any(r => r.Label == "Logitech G Pro")));

        Assert.Contains(page.Sections, s => s.Title == "Atividade recente" && s.Rows.Count == 1);
    }

    [Fact]
    public async Task LeavingThePageStopsListeningToTheActivity()
    {
        var page = Create();
        page.OnNavigatedTo();
        await WaitUntilAsync(() => _usb.Reads >= 1);
        page.OnNavigatedFrom();
        var reads = _usb.Reads;

        _activity.Add(new ActivityEntry(DateTimeOffset.UtcNow, "Icon.Devices", "Dispositivo USB conectado", "Headset"));
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(reads, _usb.Reads);
    }

    [Fact]
    public void TheBannerAppearsWhenTheWatcherCouldNotSubscribe()
    {
        _watcher.IsActive = false;

        Assert.True(Create().WatcherUnavailable);
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
}
