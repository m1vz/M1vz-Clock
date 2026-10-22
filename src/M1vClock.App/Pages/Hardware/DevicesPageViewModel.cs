using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Services;
using M1vClock.Hardware.Devices;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Pages.Hardware;

public sealed class DevicesPageViewModel : InfoPageViewModel
{
    private readonly IUsbDeviceService _usb;
    private readonly IDeviceWatcher _watcher;
    private readonly ActivityLog _activity;
    private readonly AppSettingsService _settings;
    private readonly InfoSectionViewModel _problems;
    private readonly InfoSectionViewModel _connected;
    private readonly InfoSectionViewModel _recent;

    public DevicesPageViewModel(
        IUsbDeviceService usb,
        IDeviceWatcher watcher,
        ActivityLog activity,
        AppSettingsService settings,
        ILogger<DevicesPageViewModel> logger)
        : base(PageCatalog.Find("devices"), logger)
    {
        ArgumentNullException.ThrowIfNull(usb);
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(settings);
        _usb = usb;
        _watcher = watcher;
        _activity = activity;
        _settings = settings;
        _problems = new InfoSectionViewModel(Loc.Get("Hw.Dev.Problem"), "Icon.Devices");
        _connected = new InfoSectionViewModel(Loc.Get("Hw.Dev.Connected"), "Icon.Devices");
        _recent = new InfoSectionViewModel(Loc.Get("Hw.Dev.Activity"), "Icon.History");
        Add(_connected);
        Add(_recent);
    }

    public bool NotifyDevices
    {
        get => _settings.Current.NotifyDevices;
        set
        {
            if (value == _settings.Current.NotifyDevices)
            {
                return;
            }

            _settings.Update(s => s.NotifyDevices = value);
            OnPropertyChanged();
        }
    }

    public bool WatcherUnavailable => !_watcher.IsActive;

    public override void OnNavigatedTo()
    {
        _activity.Changed += OnActivityChanged;
        OnPropertyChanged(nameof(WatcherUnavailable));
        base.OnNavigatedTo();
    }

    public override void OnNavigatedFrom()
    {
        _activity.Changed -= OnActivityChanged;
        base.OnNavigatedFrom();
    }

    protected override async Task LoadAsync(CancellationToken cancellationToken)
    {
        _connected.SetLoading();
        var result = await _usb.ReadAsync(cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        OnPropertyChanged(nameof(WatcherUnavailable));
        TrimSectionsTo(0);
        if (!result.TryGet(out var devices))
        {
            _connected.SetFailed(result.Error ?? Loc.Get("Hw.Common.Unknown"));
            Add(_connected);
            ShowActivity();
            return;
        }

        if (devices.Any(d => d.HasProblem))
        {
            _problems.StatusText = Loc.Get("Hw.Dev.Problem.Status");
            _problems.StatusTone = MetricState.Warning;
            _problems.SetReady(DeviceRows.WithProblem(devices));
            _problems.SetNotes([Loc.Get("Hw.Dev.Problem.Note")]);
            Add(_problems);
        }

        var rows = DeviceRows.Connected(devices);
        _connected.SetReady(rows);
        _connected.SetNotes(rows.Count == 0 ? [Loc.Get("Hw.Dev.Empty")] : []);
        Add(_connected);
        ShowActivity();
    }

    private void ShowActivity()
    {
        var entries = _activity.Entries;
        _recent.SetReady(DeviceRows.Activity(entries));
        _recent.SetNotes(entries.Count == 0 ? [Loc.Get("Hw.Dev.Activity.Empty")] : []);
        Add(_recent);
    }

    private void OnActivityChanged(object? sender, EventArgs e) => _ = RefreshAsync();
}
