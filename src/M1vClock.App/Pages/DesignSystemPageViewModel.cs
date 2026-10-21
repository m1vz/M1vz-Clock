using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.DesignSystem.Motion;
using M1vClock.App.DesignSystem.Tokens;
using M1vClock.App.Dialogs;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.Core.Journal;
using M1vClock.Core.RegistryValues;
using Microsoft.Win32;

namespace M1vClock.App.Pages;

public sealed record SwatchViewModel(string Name, string Hex, Brush Fill, string? ContrastText);

public sealed record TypeSampleViewModel(string Name, string Spec, string Sample, Style Style);

public sealed record MotionSpecViewModel(string Name, string Use, string Values);

public sealed partial class LiveRowViewModel(string name, string detail, string iconKey) : ObservableObject
{
    public string Name { get; } = name;

    public string Detail { get; } = detail;

    public string IconKey { get; } = iconKey;

    [ObservableProperty]
    public partial string Value { get; set; } = "—";
}

public sealed partial class DesignSystemPageViewModel : PageViewModel, IDisposable
{
    private static readonly RegistryValueAddress DemoAddress =
        new(RegistryHive.CurrentUser, RegistryView.Default, @"Software\m1vClock\Demonstracao", "UltimaDemonstracao");

    private readonly DialogService _dialogs;
    private readonly IChangeJournal _journal;
    private readonly NavigationService _navigation;
    private readonly DispatcherTimer _timer;
    private readonly Random _random = new(2026);
    private double _cpu = 58;
    private double _gpu = 64;
    private double _memory = 71;

    public DesignSystemPageViewModel(DialogService dialogs, IChangeJournal journal, NavigationService navigation)
        : base(PageCatalog.Find("design-system"))
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(journal);
        _dialogs = dialogs;
        _journal = journal;
        _navigation = navigation;
        LightSwatches = Swatches(Palette.Light);
        DarkSwatches = Swatches(Palette.Dark);
        TypeSamples = TypeScale.All.Select(Sample).ToList();
        MotionSpecs =
        [
            Spec("Motion.Standard", "Motion.Standard.Use", SpringSpec.Standard),
            Spec("Motion.Quick", "Motion.Quick.Use", SpringSpec.Quick),
            Spec("Motion.Gentle", "Motion.Gentle.Use", SpringSpec.Gentle),
            Spec("Motion.Press", "Motion.Press.Use", SpringSpec.Press),
            Spec("Motion.Momentum", "Motion.Momentum.Use", SpringSpec.Momentum),
        ];
        CpuSeries = new TimeSeries(Loc.Get("DS.Chart.Cpu"), TimeSpan.FromMinutes(15));
        GpuSeries = new TimeSeries(Loc.Get("DS.Chart.Gpu"), TimeSpan.FromMinutes(15));
        Rows =
        [
            new LiveRowViewModel("Tctl/Tdie", "AMD Ryzen 7 5700X", "Icon.Cpu"),
            new LiveRowViewModel("GPU Core", "AMD Radeon RX 5500 XT", "Icon.Gpu"),
            new LiveRowViewModel(Loc.Get("DS.List.MemoryUsed"), "16 GB DDR4", "Icon.Memory"),
            new LiveRowViewModel(Loc.Get("DS.List.SystemDisk"), "NX-512", "Icon.Storage"),
        ];
        Seed();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
    }

    public IReadOnlyList<SwatchViewModel> LightSwatches { get; }

    public IReadOnlyList<SwatchViewModel> DarkSwatches { get; }

    public IReadOnlyList<TypeSampleViewModel> TypeSamples { get; }

    public IReadOnlyList<MotionSpecViewModel> MotionSpecs { get; }

    public IReadOnlyList<LiveRowViewModel> Rows { get; }

    public TimeSeries CpuSeries { get; }

    public TimeSeries GpuSeries { get; }

    [ObservableProperty]
    public partial int ChartWindowIndex { get; set; }

    public double ChartWindowSeconds => ChartWindowIndex switch
    {
        1 => 300,
        2 => 900,
        _ => 60,
    };

    [ObservableProperty]
    public partial string CpuValue { get; set; } = "—";

    [ObservableProperty]
    public partial string CpuDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GpuValue { get; set; } = "—";

    [ObservableProperty]
    public partial MetricState GpuState { get; set; }

    [ObservableProperty]
    public partial string GpuStateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MemoryValue { get; set; } = "—";

    [ObservableProperty]
    public partial MetricState MemoryState { get; set; }

    [ObservableProperty]
    public partial string MemoryStateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool GameModeOn { get; set; } = true;

    [ObservableProperty]
    public partial bool GameDvrOn { get; set; }

    [ObservableProperty]
    public partial bool HagsOn { get; set; } = true;

    [ObservableProperty]
    public partial string JournalDemoResult { get; set; } = string.Empty;

    public override void OnNavigatedTo() => _timer.Start();

    public override void OnNavigatedFrom() => _timer.Stop();

    public void Dispose() => _timer.Stop();

    partial void OnChartWindowIndexChanged(int value) => OnPropertyChanged(nameof(ChartWindowSeconds));

    [RelayCommand]
    private async Task ShowConfirmationAsync(FrameworkElement? anchor)
    {
        var request = new DialogRequest(
            Loc.Get("DS.Dialog.Title"),
            Loc.Get("DS.Dialog.Message"),
            [Loc.Get("DS.Dialog.Change1"), Loc.Get("DS.Dialog.Change2")],
            Loc.Get("DS.Dialog.Undo"),
            Loc.Get("DS.Dialog.Confirm"),
            Loc.Get("Common.Cancel"),
            DialogTone.Neutral);
        var confirmed = await _dialogs.ShowAsync(request, anchor).ConfigureAwait(true);
        JournalDemoResult = confirmed ? Loc.Get("DS.Dialog.ResultConfirmed") : Loc.Get("DS.Dialog.ResultCancelled");
    }

    [RelayCommand]
    private async Task ShowDestructiveAsync(FrameworkElement? anchor)
    {
        var request = new DialogRequest(
            Loc.Get("DS.Destructive.Title"),
            Loc.Get("DS.Destructive.Message"),
            [Loc.Get("DS.Destructive.Change")],
            Loc.Get("DS.Destructive.Undo"),
            Loc.Get("DS.Destructive.Confirm"),
            Loc.Get("Common.Cancel"),
            DialogTone.Destructive);
        await _dialogs.ShowAsync(request, anchor).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RunJournalDemoAsync(FrameworkElement? anchor)
    {
        var stamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", Loc.Culture);
        var request = new DialogRequest(
            Loc.Get("DS.Journal.Title"),
            Loc.Get("DS.Journal.Message"),
            [Loc.Format("DS.Journal.Change", DemoAddress.Display, stamp)],
            Loc.Get("DS.Journal.Undo"),
            Loc.Get("DS.Journal.Confirm"),
            Loc.Get("Common.Cancel"),
            DialogTone.Neutral);
        if (!await _dialogs.ShowAsync(request, anchor).ConfigureAwait(true))
        {
            return;
        }

        var change = RegistryValueChanges.Set(
            Loc.Get("DS.Journal.Module"),
            Loc.Get("DS.Journal.ChangeTitle"),
            DemoAddress,
            RegistryValueState.Text(stamp),
            affectsSystem: false);
        var outcome = await _journal.ApplyAsync(change, CancellationToken.None).ConfigureAwait(true);
        JournalDemoResult = outcome.Succeeded ? Loc.Get("DS.Journal.Applied") : Loc.Format("DS.Journal.Failed", outcome.Entry.Error);
    }

    [RelayCommand]
    private void OpenHistory() => _navigation.Navigate("history");

    private void Seed()
    {
        var now = ChartClock.Now;
        for (var i = 120; i > 0; i--)
        {
            Step();
            CpuSeries.Add(now - i, _cpu);
            GpuSeries.Add(now - i, _gpu);
        }

        Publish();
    }

    private void Tick()
    {
        Step();
        CpuSeries.Add(_cpu);
        GpuSeries.Add(_gpu);
        Publish();
    }

    private void Step()
    {
        _cpu = Math.Clamp(_cpu + (_random.NextDouble() - 0.48) * 3.2, 38, 92);
        _gpu = Math.Clamp(_gpu + (_random.NextDouble() - 0.47) * 2.4, 40, 88);
        _memory = Math.Clamp(_memory + (_random.NextDouble() - 0.5) * 1.5, 40, 97);
    }

    private void Publish()
    {
        var culture = Loc.Culture;
        CpuValue = _cpu.ToString("0.0", culture);
        CpuDetail = Loc.Format("DS.Metric.CpuDetail", (_cpu + 6).ToString("0", culture));
        GpuValue = _gpu.ToString("0", culture);
        GpuState = _gpu >= 80 ? MetricState.Warning : MetricState.Good;
        GpuStateText = Loc.Get(_gpu >= 80 ? "State.Warm" : "State.Normal");
        MemoryValue = _memory.ToString("0", culture);
        MemoryState = _memory >= 90 ? MetricState.Critical : _memory >= 80 ? MetricState.Warning : MetricState.Good;
        MemoryStateText = Loc.Get(MemoryState switch
        {
            MetricState.Critical => "State.Full",
            MetricState.Warning => "State.High",
            _ => "State.Normal",
        });
        Rows[0].Value = $"{_cpu.ToString("0.0", culture)} °C";
        Rows[1].Value = $"{_gpu.ToString("0.0", culture)} °C";
        Rows[2].Value = $"{(_memory / 100 * 16).ToString("0.0", culture)} GB";
        Rows[3].Value = $"{(312 + _random.NextDouble()).ToString("0.0", culture)} GB";
    }

    private static List<SwatchViewModel> Swatches(Palette palette)
    {
        var surface = palette.Surface;
        SwatchViewModel Swatch(string key, Color color, bool text = false)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var contrast = text ? Loc.Format("DS.Color.Contrast", Contrast.Ratio(color, surface).ToString("0.0", CultureInfo.GetCultureInfo("pt-BR"))) : null;
            return new SwatchViewModel(Loc.Get($"Token.{key}"), Hex(color), brush, contrast);
        }

        return
        [
            Swatch(nameof(Palette.WindowBackground), palette.WindowBackground),
            Swatch(nameof(Palette.Surface), palette.Surface),
            Swatch(nameof(Palette.FillPrimary), palette.FillPrimary),
            Swatch(nameof(Palette.Separator), palette.Separator),
            Swatch(nameof(Palette.TextPrimary), palette.TextPrimary, text: true),
            Swatch(nameof(Palette.TextSecondary), palette.TextSecondary, text: true),
            Swatch(nameof(Palette.TextTertiary), palette.TextTertiary, text: true),
            Swatch(nameof(Palette.Accent), palette.Accent),
            Swatch(nameof(Palette.AccentForeground), palette.AccentForeground, text: true),
            Swatch(nameof(Palette.Positive), palette.Positive, text: true),
            Swatch(nameof(Palette.Warning), palette.Warning, text: true),
            Swatch(nameof(Palette.Critical), palette.Critical, text: true),
        ];
    }

    private static string Hex(Color color) =>
        color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static TypeSampleViewModel Sample(TypeStyle style)
    {
        var weight = style.Weight == FontWeights.SemiBold ? "SemiBold" : style.Weight == FontWeights.Medium ? "Medium" : "Regular";
        var cut = style.Cut == OpticalCut.Display ? "Inter Display" : "Inter";
        var spec = string.Create(CultureInfo.InvariantCulture, $"{style.Size}/{style.LineHeight} · {weight} · {cut}");
        var resource = (Style)Application.Current.FindResource($"Text.{style.Name}");
        return new TypeSampleViewModel(Loc.Get($"Type.{style.Name}"), spec, Loc.Get($"Type.{style.Name}.Sample"), resource);
    }

    private static MotionSpecViewModel Spec(string nameKey, string useKey, SpringSpec spec)
    {
        var settle = SpringMath.SettleTime(spec, 0.95) * 1000;
        var values = Loc.Format(
            "Motion.Values",
            spec.DampingRatio.ToString("0.0", Loc.Culture),
            spec.Response.ToString("0.00", Loc.Culture),
            Math.Round(settle));
        return new MotionSpecViewModel(Loc.Get(nameKey), Loc.Get(useKey), values);
    }
}
