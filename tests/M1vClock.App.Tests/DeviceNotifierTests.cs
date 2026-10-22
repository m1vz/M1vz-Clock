using System.IO;
using M1vClock.App.Services;
using M1vClock.Core.Results;
using M1vClock.Core.Settings;
using M1vClock.Hardware.Devices;
using M1vClock.Hardware.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.App.Tests;

internal sealed class FakeDeviceWatcher : IDeviceWatcher
{
    public event EventHandler<DeviceBatch>? DevicesChanged;

    public event EventHandler<VolumeEvent>? VolumeChanged;

    public bool IsActive { get; set; } = true;

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public Exception? StartFailure { get; set; }

    public TaskCompletionSource StartedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Started = true;
        StartedSignal.TrySetResult();
        return StartFailure is null ? Task.CompletedTask : Task.FromException(StartFailure);
    }

    public void Dispose() => Disposed = true;

    public void Raise(DeviceBatch batch) => DevicesChanged?.Invoke(this, batch);

    public void Raise(VolumeEvent volume) => VolumeChanged?.Invoke(this, volume);

    public bool HasSubscribers => DevicesChanged is not null || VolumeChanged is not null;
}

internal sealed class VolumeStorage : IStorageService
{
    public IReadOnlyList<Volume> Volumes { get; set; } = [];

    public int Reads { get; private set; }

    public Task<StorageReport> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(new StorageReport(
            ReadResult.Success<IReadOnlyList<DiskReport>>([]),
            ReadResult.Success(Volumes),
            CounterAccess.Read,
            CounterAccess.Read));
    }
}

internal sealed class RecordingToasts : INotificationService
{
    public List<ToastRequest> Shown { get; } = [];

    public void Show(ToastRequest request) => Shown.Add(request);
}

public sealed class DeviceNotifierTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("m1vclock-notifier-tests");
    private readonly FakeDeviceWatcher _watcher = new();
    private readonly VolumeStorage _storage = new();
    private readonly RecordingToasts _toasts = new();
    private readonly ActivityLog _activity = new(new InlineUiThread());
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly AppSettingsService _settings;

    public DeviceNotifierTests()
    {
        var store = new SettingsStore<AppSettings>(Path.Combine(_temp.FullName, "settings.json"), AppSettings.CurrentSchemaVersion, [], NullLogger.Instance);
        _settings = new AppSettingsService(store, NullLogger<AppSettingsService>.Instance);
    }

    public void Dispose() => _temp.Delete(recursive: true);

    private static UsbDeviceInfo Device(string name, string? maker = null) =>
        new("USB\\VID_046D&PID_C52B\\A", name, "USB", UsbCategory.Generic, "046D", "C52B", maker, null, null);

    private static Volume Pen(string letter = "E:", string label = "KINGSTON") =>
        new(letter, label, VolumeKind.Removable, "FAT32", "Kingston DataTraveler", 30_000_000_000, 12_000_000_000);

    private DeviceNotifier Create(CollectingLogger<DeviceNotifier>? logger = null) =>
        new(_watcher, _storage, _toasts, _activity, _settings, _clock, logger ?? new CollectingLogger<DeviceNotifier>());

    private static DeviceBatch Batch(UsbDeviceInfo[] added, UsbDeviceInfo[] removed) => new(new DeviceDiff(added, removed), false, Now);

    private async Task StartAsync(DeviceNotifier notifier)
    {
        await notifier.StartAsync(TestContext.Current.CancellationToken);
        await _watcher.StartedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => _storage.Reads > 0);
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
    public async Task StartingStartsTheWatcherAndReadsTheVolumes()
    {
        var notifier = Create();

        await StartAsync(notifier);

        Assert.True(_watcher.Started);
        Assert.Equal(1, _storage.Reads);
    }

    [Fact]
    public async Task AConnectedUsbDeviceBecomesAnActivityEntryAndAToast()
    {
        await StartAsync(Create());

        _watcher.Raise(Batch([Device("Receptor Unifying", "Logitech")], []));

        var toast = Assert.Single(_toasts.Shown);
        Assert.Equal("Dispositivo USB conectado", toast.Title);
        Assert.Equal("Receptor Unifying · Logitech", toast.Text);
        Assert.Equal(ToastTone.Success, toast.Tone);
        var entry = Assert.Single(_activity.Entries);
        Assert.Equal(toast.Title, entry.Title);
        Assert.Equal(Now, entry.At);
    }

    [Fact]
    public async Task ARemovedUsbDeviceIsAnnouncedAsDisconnected()
    {
        await StartAsync(Create());

        _watcher.Raise(Batch([], [Device("Headset")]));

        var toast = Assert.Single(_toasts.Shown);
        Assert.Equal("Dispositivo USB desconectado", toast.Title);
        Assert.Equal("Headset", toast.Text);
    }

    [Fact]
    public async Task TheMakerIsNotRepeatedWhenTheNameAlreadyHasIt()
    {
        await StartAsync(Create());

        _watcher.Raise(Batch([Device("Logitech G Pro", "Logitech")], []));

        Assert.Equal("Logitech G Pro", Assert.Single(_toasts.Shown).Text);
    }

    [Fact]
    public async Task WithAlertsOffTheActivityIsStillRecordedButNoToastAppears()
    {
        _settings.Update(s => s.NotifyDevices = false);
        await StartAsync(Create());

        _watcher.Raise(Batch([Device("Headset")], []));

        Assert.Empty(_toasts.Shown);
        Assert.Single(_activity.Entries);
    }

    [Fact]
    public async Task AStorageDeviceOrAVolumeRelatedBatchGivesNoUsbToast()
    {
        await StartAsync(Create());
        var stick = Device("USB Mass Storage Device") with { Category = UsbCategory.Storage };

        _watcher.Raise(Batch([stick], []));
        _watcher.Raise(new DeviceBatch(new DeviceDiff([Device("Receptor")], []), VolumeJustNow: true, Now));

        Assert.Empty(_toasts.Shown);
        Assert.Empty(_activity.Entries);
    }

    [Fact]
    public async Task AnArrivingDriveIsDescribedAfterWindowsMountsIt()
    {
        await StartAsync(Create());
        _storage.Volumes = [Pen()];

        _watcher.Raise(new VolumeEvent("E:", true, Now));
        Assert.Empty(_toasts.Shown);
        _clock.Advance(DeviceNotifier.MountDelay);
        await WaitUntilAsync(() => _toasts.Shown.Count == 1);

        var toast = Assert.Single(_toasts.Shown);
        Assert.Equal("Pendrive conectado", toast.Title);
        Assert.Equal("KINGSTON (E:) · 12 GB livres de 30 GB", toast.Text);
        Assert.Equal("Icon.Storage", toast.IconKey);
    }

    [Fact]
    public async Task ARemovedDriveSaysItCanBeUnpluggedUsingTheKnownLabel()
    {
        _storage.Volumes = [Pen()];
        await StartAsync(Create());
        _storage.Volumes = [];

        _watcher.Raise(new VolumeEvent("E:", false, Now));
        await WaitUntilAsync(() => _toasts.Shown.Count == 1);

        var toast = Assert.Single(_toasts.Shown);
        Assert.Equal("Unidade removida", toast.Title);
        Assert.Equal("KINGSTON (E:) pode ser desconectada.", toast.Text);
    }

    [Fact]
    public async Task AnUnknownRemovedDriveFallsBackToTheLetter()
    {
        await StartAsync(Create());

        _watcher.Raise(new VolumeEvent("F:", false, Now));
        await WaitUntilAsync(() => _toasts.Shown.Count == 1);

        Assert.Equal("F: removida.", Assert.Single(_toasts.Shown).Text);
    }

    [Fact]
    public async Task AWatcherThatCannotSubscribeIsLoggedAsUnavailable()
    {
        _watcher.IsActive = false;
        var logger = new CollectingLogger<DeviceNotifier>();

        await StartAsync(Create(logger));

        Assert.Contains(logger.Entries, e => e.EventId.Id == 2018);
    }

    [Fact]
    public async Task AnUnexpectedStartFailureGoesToTheLogAndDoesNotCrash()
    {
        _watcher.StartFailure = new InvalidOperationException("WMI indisponível.");
        var logger = new CollectingLogger<DeviceNotifier>();
        var notifier = Create(logger);

        await notifier.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => logger.Entries.Count > 0);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(2019, entry.EventId.Id);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task StoppingUnsubscribesAndReleasesTheWatcher()
    {
        var notifier = Create();
        await StartAsync(notifier);

        await notifier.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(_watcher.HasSubscribers);
        Assert.True(_watcher.Disposed);
    }

    [Fact]
    public void TheMessagesAreBuiltFromPlainData()
    {
        Assert.Equal("Unidade conectada", DeviceNotifier.VolumeArrivedMessage("G:", null).Title);
        Assert.Equal("G:", DeviceNotifier.VolumeArrivedMessage("G:", null).Text);
        Assert.Equal("Disco externo conectado", DeviceNotifier.VolumeArrivedMessage("G:", Pen() with { Kind = VolumeKind.External }).Title);
        Assert.Equal("Unidade conectada", DeviceNotifier.VolumeArrivedMessage("G:", Pen() with { Kind = VolumeKind.Internal }).Title);
        Assert.StartsWith("E: · ", DeviceNotifier.VolumeArrivedMessage("E:", Pen(label: string.Empty)).Text, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => DeviceNotifier.VolumeRemovedMessage(" ", null));
    }
}
