using M1vClock.Core.Results;
using M1vClock.Hardware.Devices;
using Microsoft.Extensions.Time.Testing;

namespace M1vClock.Hardware.Tests;

internal sealed class FakeUsbService : IUsbDeviceService
{
    private ReadResult<IReadOnlyList<UsbDeviceInfo>> _current = ReadResult.Success<IReadOnlyList<UsbDeviceInfo>>([]);

    public int Reads { get; private set; }

    public void Set(params UsbDeviceInfo[] devices) => _current = ReadResult.Success<IReadOnlyList<UsbDeviceInfo>>(devices);

    public void Fail(string error) => _current = ReadResult.Failure<IReadOnlyList<UsbDeviceInfo>>(error);

    public Task<ReadResult<IReadOnlyList<UsbDeviceInfo>>> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(_current);
    }
}

internal sealed class FakeChangeSource : IDeviceChangeSource
{
    public int Starts { get; private set; }

    public bool Disposed { get; private set; }

    public bool IsActive { get; set; } = true;

    public event EventHandler? DevicesChanged;

    public event EventHandler<VolumeEvent>? VolumeChanged;

    public void Start() => Starts++;

    public void Dispose() => Disposed = true;

    public void RaiseDevices() => DevicesChanged?.Invoke(this, EventArgs.Empty);

    public void RaiseVolume(VolumeEvent volume) => VolumeChanged?.Invoke(this, volume);

    public bool HasDeviceSubscribers => DevicesChanged is not null;
}

public sealed class DeviceChangeDetectorTests
{
    private static UsbDeviceInfo Device(string id, string name, UsbCategory category = UsbCategory.Generic, string? port = null, string? maker = null) =>
        new($"USB\\VID_0000&PID_0000\\{id}", name, "USB", category, "0000", "0000", maker, port, null);

    [Fact]
    public void DiffFindsWhatWasAddedAndWhatWasRemovedByInstanceId()
    {
        var mouse = Device("A", "Mouse");
        var keyboard = Device("B", "Teclado");
        var headset = Device("C", "Headset");

        var diff = DeviceChangeDetector.Diff([mouse, keyboard], [keyboard, headset]);

        Assert.Equal([headset], diff.Added);
        Assert.Equal([mouse], diff.Removed);
        Assert.False(diff.IsEmpty);
    }

    [Fact]
    public void TheSameDevicesAreAnEmptyDiffEvenWithDifferentCase()
    {
        var before = Device("abc", "Mouse");
        var after = before with { InstanceId = before.InstanceId.ToUpperInvariant() };

        Assert.True(DeviceChangeDetector.Diff([before], [after]).IsEmpty);
    }

    [Fact]
    public void ThePortBeatsTheMakerWhichBeatsEverythingElse()
    {
        var hub = Device("1", "Hub", UsbCategory.Hub);
        var named = Device("2", "Receptor", maker: "Logitech");
        var board = Device("3", "Arduino (COM4)", UsbCategory.SerialPort, port: "COM4");

        Assert.Same(board, DeviceChangeDetector.MostDescriptive([hub, named, board]));
        Assert.Same(named, DeviceChangeDetector.MostDescriptive([hub, named]));
        Assert.Same(hub, DeviceChangeDetector.MostDescriptive([hub]));
        Assert.Null(DeviceChangeDetector.MostDescriptive([]));
    }

    [Fact]
    public void AHubIsOnlyAnnouncedWhenItIsTheOnlyThingThatChanged()
    {
        var hub = Device("1", "Hub", UsbCategory.Hub);
        var pad = Device("2", "Gamepad");

        Assert.Same(pad, DeviceChangeDetector.MostDescriptive([hub, pad]));
    }

    [Fact]
    public void StorageIsLeftToTheVolumeNotice()
    {
        var at = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var stick = Device("1", "USB Mass Storage Device", UsbCategory.Storage);
        var batch = new DeviceBatch(new DeviceDiff([stick], []), VolumeJustNow: false, at);

        Assert.Empty(DeviceChangeDetector.Announcements(batch));
    }

    [Fact]
    public void NothingIsAnnouncedRightAfterAVolumeEvent()
    {
        var at = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var batch = new DeviceBatch(new DeviceDiff([Device("1", "Receptor")], []), VolumeJustNow: true, at);

        Assert.Empty(DeviceChangeDetector.Announcements(batch));
    }

    [Fact]
    public void AConnectionAndARemovalInTheSameBatchAreBothAnnounced()
    {
        var at = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        var added = Device("1", "Headset");
        var removed = Device("2", "Mouse");

        var changes = DeviceChangeDetector.Announcements(new DeviceBatch(new DeviceDiff([added], [removed]), false, at));

        Assert.Equal([DeviceChangeKind.Connected, DeviceChangeKind.Disconnected], changes.Select(c => c.Kind));
        Assert.Equal([added, removed], changes.Select(c => c.Device));
        Assert.All(changes, c => Assert.Equal(at, c.At));
    }
}

public sealed class DeviceWatcherTests
{
    private static UsbDeviceInfo Device(string id, string name) =>
        new($"USB\\VID_046D&PID_C52B\\{id}", name, "USB", UsbCategory.Generic, "046D", "C52B", "Logitech", null, null);

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
            Watcher = new DeviceWatcher(Usb, Source, Clock, new CollectingLogger<DeviceWatcher>());
            Watcher.DevicesChanged += (_, batch) => Batches.Add(batch);
            Watcher.VolumeChanged += (_, volume) => Volumes.Add(volume);
        }

        public FakeUsbService Usb { get; } = new();

        public FakeChangeSource Source { get; } = new();

        public FakeTimeProvider Clock { get; }

        public DeviceWatcher Watcher { get; }

        public List<DeviceBatch> Batches { get; } = [];

        public List<VolumeEvent> Volumes { get; } = [];

        public void Dispose() => Watcher.Dispose();
    }

    [Fact]
    public async Task StartingReadsTheBaselineWithoutAnnouncingAnything()
    {
        using var harness = new Harness();
        harness.Usb.Set(Device("A", "Mouse"));

        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.Source.Starts);
        Assert.Equal(1, harness.Usb.Reads);
        Assert.Empty(harness.Batches);
        Assert.True(harness.Watcher.IsActive);
    }

    [Fact]
    public async Task StartingTwiceStartsTheSourceOnce()
    {
        using var harness = new Harness();

        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, harness.Source.Starts);
        Assert.Equal(1, harness.Usb.Reads);
    }

    [Fact]
    public async Task APluggedDeviceIsReportedOnlyAfterTheDebounce()
    {
        using var harness = new Harness();
        harness.Usb.Set(Device("A", "Mouse"));
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);
        harness.Usb.Set(Device("A", "Mouse"), Device("B", "Headset"));

        harness.Source.RaiseDevices();
        harness.Clock.Advance(DeviceWatcher.Debounce - TimeSpan.FromMilliseconds(1));
        Assert.Empty(harness.Batches);
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1));

        var batch = Assert.Single(harness.Batches);
        Assert.Equal(["Headset"], batch.Diff.Added.Select(d => d.Name));
        Assert.Empty(batch.Diff.Removed);
        Assert.False(batch.VolumeJustNow);
        Assert.Equal(harness.Clock.GetUtcNow(), batch.At);
    }

    [Fact]
    public async Task ABurstOfEventsBecomesOneReadAfterTheLastOne()
    {
        using var harness = new Harness();
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);
        harness.Usb.Set(Device("B", "Headset"));

        harness.Source.RaiseDevices();
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1000));
        harness.Source.RaiseDevices();
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1000));
        harness.Source.RaiseDevices();
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1499));
        Assert.Empty(harness.Batches);
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Single(harness.Batches);
        Assert.Equal(2, harness.Usb.Reads);
    }

    [Fact]
    public async Task AnUnpluggedDeviceIsReportedAsRemoved()
    {
        using var harness = new Harness();
        harness.Usb.Set(Device("A", "Mouse"), Device("B", "Headset"));
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);
        harness.Usb.Set(Device("A", "Mouse"));

        harness.Source.RaiseDevices();
        harness.Clock.Advance(DeviceWatcher.Debounce);

        var batch = Assert.Single(harness.Batches);
        Assert.Equal(["Headset"], batch.Diff.Removed.Select(d => d.Name));
        Assert.Empty(batch.Diff.Added);
    }

    [Fact]
    public async Task ANoiseEventWithNoRealChangeAnnouncesNothing()
    {
        using var harness = new Harness();
        harness.Usb.Set(Device("A", "Mouse"));
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);

        harness.Source.RaiseDevices();
        harness.Clock.Advance(DeviceWatcher.Debounce);

        Assert.Empty(harness.Batches);
        Assert.Equal(2, harness.Usb.Reads);
    }

    [Fact]
    public async Task ADeviceChangeRightAfterAVolumeEventIsMarkedAsVolumeRelated()
    {
        using var harness = new Harness();
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);
        harness.Source.RaiseVolume(new VolumeEvent("E:", true, harness.Clock.GetUtcNow()));
        harness.Usb.Set(Device("S", "USB Mass Storage Device"));

        harness.Source.RaiseDevices();
        harness.Clock.Advance(DeviceWatcher.Debounce);

        Assert.True(Assert.Single(harness.Batches).VolumeJustNow);
        Assert.Equal("E:", Assert.Single(harness.Volumes).DriveLetter);
    }

    [Fact]
    public async Task AVolumeEventOlderThanTheWindowNoLongerSilencesDevices()
    {
        using var harness = new Harness();
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);
        harness.Source.RaiseVolume(new VolumeEvent("E:", true, harness.Clock.GetUtcNow()));
        harness.Clock.Advance(DeviceWatcher.VolumeWindow + TimeSpan.FromSeconds(1));
        harness.Usb.Set(Device("B", "Headset"));

        harness.Source.RaiseDevices();
        harness.Clock.Advance(DeviceWatcher.Debounce);

        Assert.False(Assert.Single(harness.Batches).VolumeJustNow);
    }

    [Fact]
    public async Task AFailedReadKeepsTheOldSnapshotAndTheNextGoodReadCatchesUp()
    {
        using var harness = new Harness();
        harness.Usb.Set(Device("A", "Mouse"));
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);
        harness.Usb.Fail("WMI indisponível.");

        harness.Source.RaiseDevices();
        harness.Clock.Advance(DeviceWatcher.Debounce);
        Assert.Empty(harness.Batches);

        harness.Usb.Set(Device("A", "Mouse"), Device("B", "Headset"));
        harness.Source.RaiseDevices();
        harness.Clock.Advance(DeviceWatcher.Debounce);

        Assert.Equal(["Headset"], Assert.Single(harness.Batches).Diff.Added.Select(d => d.Name));
    }

    [Fact]
    public async Task IfTheBaselineFailedTheFirstGoodReadDoesNotAnnounceEverythingAsNew()
    {
        using var harness = new Harness();
        harness.Usb.Fail("WMI indisponível.");
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);
        harness.Usb.Set(Device("A", "Mouse"), Device("B", "Headset"));

        harness.Source.RaiseDevices();
        harness.Clock.Advance(DeviceWatcher.Debounce);

        Assert.Empty(harness.Batches);
    }

    [Fact]
    public async Task DisposingStopsListeningAndReleasesTheSource()
    {
        var harness = new Harness();
        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);
        harness.Usb.Set(Device("B", "Headset"));
        harness.Source.RaiseDevices();

        harness.Watcher.Dispose();
        harness.Clock.Advance(DeviceWatcher.Debounce);
        harness.Source.RaiseDevices();
        harness.Clock.Advance(DeviceWatcher.Debounce);

        Assert.True(harness.Source.Disposed);
        Assert.False(harness.Source.HasDeviceSubscribers);
        Assert.Empty(harness.Batches);
        harness.Watcher.Dispose();
    }

    [Fact]
    public async Task ASourceThatCouldNotStartMakesTheWatcherInactive()
    {
        using var harness = new Harness();
        harness.Source.IsActive = false;

        await harness.Watcher.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(harness.Watcher.IsActive);
    }
}
