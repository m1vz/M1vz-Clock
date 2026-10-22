using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.DesignSystem.Motion;
using M1vClock.App.Dialogs;
using M1vClock.App.Hosting;
using M1vClock.App.Localization;
using M1vClock.App.Navigation;
using M1vClock.App.Search;
using M1vClock.App.Services;
using M1vClock.Core.Diagnostics;
using M1vClock.Core.Paths;
using M1vClock.Core.Shell;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Shell;

public partial class MainWindow : Window
{
    private const double OverlayScale = 0.96;
    private const double DialogStartScale = 0.9;
    private const double PageOffset = 16;
    private static readonly TimeSpan MinimumScrollWait = TimeSpan.FromMilliseconds(500);

    private readonly MainViewModel _viewModel;
    private readonly ThemeService _theme;
    private readonly IShellLauncher _shell;
    private readonly AppPaths _paths;
    private readonly ILogger<MainWindow> _logger;
    private readonly PillState _mainPill = new();
    private readonly PillState _footerPill = new();
    private HwndSource? _source;
    private ContentPresenter? _currentView;
    private IInputElement? _focusBeforeOverlay;
    private bool _paletteVisible;
    private bool _dialogVisible;
    private bool _maximizeHover;
    private bool _maximizePressed;
    private bool _navigating;
    private bool _materialLogged;
    private readonly HeavyTestGate _gate;

    public MainWindow(MainViewModel viewModel, ThemeService theme, IShellLauncher shell, AppPaths paths, HeavyTestGate gate, ILogger<MainWindow> logger)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(theme);
        _viewModel = viewModel;
        _theme = theme;
        _shell = shell;
        _paths = paths;
        _gate = gate;
        _logger = logger;
        OpenPaletteCommand = new RelayCommand(OpenPalette);
        InitializeComponent();
        DataContext = viewModel;

        viewModel.Navigation.Navigated += OnNavigated;
        viewModel.Palette.PropertyChanged += OnPalettePropertyChanged;
        viewModel.Dialogs.Opened += OnDialogOpened;
        viewModel.Dialogs.Closing += OnDialogClosing;
        theme.Changing += OnThemeChanging;
        theme.Changed += OnThemeChanged;
        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => UpdateWindowState();
        PreviewKeyDown += OnWindowPreviewKeyDown;
        LayoutUpdated += (_, _) => UpdatePills();
        Closed += OnClosed;
    }

    public ICommand OpenPaletteCommand { get; }

    public void Start(StartupOptions options, PreviousSession? previous)
    {
        ArgumentNullException.ThrowIfNull(options);
        _viewModel.Navigation.Navigate(LandingPage(options.Page));
        if (options.Scroll > 0)
        {
            _ = ScrollLaterAsync(options);
        }

        if (previous is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _ = ShowPreviousSessionAsync(previous));
        }

        if (options.Overlay == "palette")
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, OpenPalette);
        }
        else if (options.Overlay == "toast")
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, ShowSampleToasts);
        }
        else if (options.Overlay == "dialog")
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _ = _viewModel.Dialogs.ShowAsync(SampleDialog(), SearchButton));
        }

        if (options.ScreenshotPath is { } path)
        {
            _ = CaptureAndCloseAsync(path, options.ScreenshotDelay);
        }
    }

    private async Task ScrollLaterAsync(StartupOptions options)
    {
        var wait = options.ScreenshotPath is null ? TimeSpan.FromSeconds(1) : options.ScreenshotDelay - TimeSpan.FromSeconds(1);
        await Task.Delay(wait < MinimumScrollWait ? MinimumScrollWait : wait).ConfigureAwait(true);
        FindDescendant<PageScaffold>(PageHost)?.ScrollTo(options.Scroll);
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void ShowUnexpectedError()
    {
        if (_dialogVisible)
        {
            return;
        }

        _ = _viewModel.Dialogs.ShowAsync(new DialogRequest(
            Loc.Get("Error.Unexpected.Title"),
            Loc.Get("Error.Unexpected.Message"),
            [],
            null,
            Loc.Get("Common.Ok"),
            null,
            DialogTone.Error));
    }

    private static string LandingPage(string? requested)
    {
        if (requested is not null && PageCatalog.All.Any(p => p.Id == requested))
        {
            return requested;
        }

        return PageCatalog.Find("overview").IsAvailable ? "overview" : "design-system";
    }

    private void ShowSampleToasts()
    {
        _viewModel.Notifications.Show(new ToastRequest(Loc.Get("Hw.Dev.Toast.PenDrive"), Loc.Format("Hw.Dev.Toast.VolumeText", "KINGSTON (E:)", "12 GB", "30 GB"), "Icon.Storage", ToastTone.Success));
        _viewModel.Notifications.Show(new ToastRequest(Loc.Get("Hw.Dev.Toast.UsbConnected"), "Logitech G Pro · Logitech", "Icon.Devices", ToastTone.Info));
    }

    private static DialogRequest SampleDialog() => new(
        Loc.Get("DS.Dialog.Title"),
        Loc.Get("DS.Dialog.Message"),
        [Loc.Get("DS.Dialog.Change1"), Loc.Get("DS.Dialog.Change2")],
        Loc.Get("DS.Dialog.Undo"),
        Loc.Get("DS.Dialog.Confirm"),
        Loc.Get("Common.Cancel"),
        DialogTone.Neutral);

    private async Task ShowPreviousSessionAsync(PreviousSession previous)
    {
        var started = previous.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Loc.Culture);
        var request = new DialogRequest(
            Loc.Get("Crash.Title"),
            Loc.Format("Crash.Message", started),
            [],
            null,
            Loc.Get("Crash.OpenLogs"),
            Loc.Get("Common.Close"),
            DialogTone.Error);
        if (await _viewModel.Dialogs.ShowAsync(request).ConfigureAwait(true))
        {
            OpenLogs();
        }
    }

    private void OpenLogs()
    {
        try
        {
            _shell.OpenFolder(_paths.EnsureDirectory(_paths.Logs));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or System.Runtime.InteropServices.COMException)
        {
            AppLog.ActionFailed(_logger, ex, "abrir a pasta de logs");
        }
    }

    private async Task CaptureAndCloseAsync(string path, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(true);
        BringToFront();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(250).ConfigureAwait(true);
        try
        {
            var image = _source is null ? null : WindowInterop.CaptureScreenRegion(_source.Handle);
            image ??= WindowInterop.Render(Root);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            AppLog.ScreenshotSaved(_logger, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            AppLog.ActionFailed(_logger, ex, "gravar a captura de tela");
        }

        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source?.AddHook(WndProc);
        ApplyBackdrop();
        UpdateWindowState();
    }

    private void ApplyBackdrop()
    {
        if (_source is null)
        {
            return;
        }

        var hwnd = _source.Handle;
        WindowInterop.TrySetDarkMode(hwnd, _theme.IsDark);
        var useMaterial = _theme.UseMaterial;
        string? reason = null;
        if (useMaterial)
        {
            if (_source.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Transparent;
            }

            var result = WindowInterop.ExtendFrameIntoClient(hwnd);
            if (result == 0)
            {
                result = WindowInterop.SetMica(hwnd, true);
            }

            if (result != 0)
            {
                useMaterial = false;
                reason = $"DwmSetWindowAttribute retornou 0x{result:X8}";
            }
        }
        else
        {
            reason = SystemPreferences.SupportsMica ? "transparência ou alto contraste desativam o material" : "Windows anterior ao 22H2";
            if (SystemPreferences.SupportsMica)
            {
                WindowInterop.SetMica(hwnd, false);
            }
        }

        if (useMaterial)
        {
            Root.Background = Brushes.Transparent;
        }
        else
        {
            Root.SetResourceReference(Panel.BackgroundProperty, "Brush.ChromeFallback");
            if (!_materialLogged && reason is not null)
            {
                AppLog.MaterialUnavailable(_logger, reason);
                _materialLogged = true;
            }
        }
    }

    private void UpdateWindowState()
    {
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        var label = Loc.Get(maximized ? "Window.Restore" : "Window.Maximize");
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, label);
        MaximizeButton.ToolTip = label;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WindowInterop.WmNcHitTest:
                if (IsOverMaximize(lParam))
                {
                    SetMaximizeHover(true);
                    handled = true;
                    return WindowInterop.HtMaxButton;
                }

                SetMaximizeHover(false);
                break;
            case WindowInterop.WmNcLButtonDown when wParam.ToInt64() == WindowInterop.HtMaxButton:
                _maximizePressed = true;
                handled = true;
                return IntPtr.Zero;
            case WindowInterop.WmNcLButtonUp when wParam.ToInt64() == WindowInterop.HtMaxButton:
                if (_maximizePressed)
                {
                    ToggleMaximize();
                }

                _maximizePressed = false;
                handled = true;
                return IntPtr.Zero;
            case WindowInterop.WmNcMouseLeave:
                SetMaximizeHover(false);
                _maximizePressed = false;
                break;
        }

        return IntPtr.Zero;
    }

    private bool IsOverMaximize(IntPtr lParam)
    {
        if (!MaximizeButton.IsVisible || PresentationSource.FromVisual(MaximizeButton) is null)
        {
            return false;
        }

        var point = MaximizeButton.PointFromScreen(WindowInterop.PointFromLParam(lParam));
        return point.X >= 0 && point.Y >= 0 && point.X < MaximizeButton.ActualWidth && point.Y < MaximizeButton.ActualHeight;
    }

    private void SetMaximizeHover(bool value)
    {
        if (_maximizeHover == value)
        {
            return;
        }

        _maximizeHover = value;
        Assist.SetIsSelected(MaximizeButton, value);
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnNavigated(object? sender, NavigatedEventArgs e)
    {
        _navigating = true;
        ShowPage(e.Page, e.Direction);
        UpdatePills();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RevealSelectedItem);
    }

    private void RevealSelectedItem()
    {
        var selected = _viewModel.AllItems.FirstOrDefault(i => i.IsSelected);
        if (selected is null)
        {
            return;
        }

        var button = FindButton(SectionList, selected) ?? FindButton(FooterList, selected);
        button?.BringIntoView();
    }

    private void ShowPage(PageViewModel page, int direction)
    {
        var offset = new TranslateTransform();
        var view = new ContentPresenter { Content = page, RenderTransform = offset, Opacity = 0, Focusable = false };
        PageHost.Children.Add(view);

        var previous = _currentView;
        _currentView = view;
        if (previous is not null)
        {
            previous.IsHitTestVisible = false;
            if (previous.RenderTransform is TranslateTransform previousOffset)
            {
                SpringAnimator.Animate(previousOffset, TranslateTransform.YProperty, -direction * PageOffset, SpringSpec.Standard);
            }

            SpringAnimator.Animate(previous, OpacityProperty, 0, SpringSpec.Quick, completed: () => PageHost.Children.Remove(previous));
        }

        SpringAnimator.Set(offset, TranslateTransform.YProperty, direction * PageOffset);
        SpringAnimator.Animate(offset, TranslateTransform.YProperty, 0, SpringSpec.Standard);
        SpringAnimator.Animate(view, OpacityProperty, 1, SpringSpec.Standard);
    }

    private void UpdatePills()
    {
        var selected = _viewModel.AllItems.FirstOrDefault(i => i.IsSelected);
        var section = selected is null ? null : _viewModel.Sections.FirstOrDefault(s => s.Items.Contains(selected));
        var animate = _navigating;
        _navigating = false;
        PositionPill(_mainPill, MainPill, MainPillOffset, MainPillLayer, SectionList, section is null ? null : selected, section?.IsExpanded ?? false, animate);
        PositionPill(_footerPill, FooterPill, FooterPillOffset, FooterPillLayer, FooterList, section is null ? selected : null, true, animate);
    }

    private static void PositionPill(PillState state, Border pill, TranslateTransform offset, Canvas layer, ItemsControl list, SidebarItemViewModel? item, bool expanded, bool animate)
    {
        var button = item is null ? null : FindButton(list, item);
        var visible = button is { IsVisible: true } && expanded;
        if (button is { IsVisible: true } && PresentationSource.FromVisual(button) is not null && PresentationSource.FromVisual(layer) is not null)
        {
            var origin = button.TransformToVisual(layer).Transform(default);
            if (Math.Abs(pill.Width - button.ActualWidth) > 0.5 || double.IsNaN(pill.Width))
            {
                pill.Width = button.ActualWidth;
            }

            if (Math.Abs(Canvas.GetLeft(pill) - origin.X) > 0.5 || double.IsNaN(Canvas.GetLeft(pill)))
            {
                Canvas.SetLeft(pill, origin.X);
            }

            if (double.IsNaN(state.TargetY) || Math.Abs(state.TargetY - origin.Y) > 0.5)
            {
                var follow = animate || SpringAnimator.IsAnimating(offset, TranslateTransform.YProperty);
                if (follow && pill.Opacity > 0.01 && !double.IsNaN(state.TargetY))
                {
                    SpringAnimator.Animate(offset, TranslateTransform.YProperty, origin.Y, SpringSpec.Standard, precision: 0.1);
                }
                else
                {
                    SpringAnimator.Set(offset, TranslateTransform.YProperty, origin.Y);
                }

                state.TargetY = origin.Y;
            }
        }

        var opacity = visible ? 1.0 : 0.0;
        if (state.TargetOpacity != opacity)
        {
            state.TargetOpacity = opacity;
            SpringAnimator.Animate(pill, OpacityProperty, opacity, SpringSpec.Quick);
        }
    }

    private static Button? FindButton(DependencyObject root, SidebarItemViewModel item)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && ReferenceEquals(button.Tag, item))
            {
                return button;
            }

            if (FindButton(child, item) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OpenPalette()
    {
        if (_dialogVisible)
        {
            return;
        }

        if (_viewModel.Palette.IsOpen)
        {
            PaletteInput.Focus();
            PaletteInput.SelectAll();
            return;
        }

        _viewModel.Palette.Open();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => OpenPalette();

    private void OnPalettePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen))
        {
            if (_viewModel.Palette.IsOpen)
            {
                ShowPalette();
            }
            else
            {
                HidePalette();
            }
        }
        else if (e.PropertyName == nameof(CommandPaletteViewModel.Selected) && _viewModel.Palette.Selected is { } selected)
        {
            PaletteResults.ScrollIntoView(selected);
        }
    }

    private void ShowPalette()
    {
        _paletteVisible = true;
        RememberFocus();
        ShowOverlay();
        PaletteCard.Visibility = Visibility.Visible;
        PaletteCard.IsHitTestVisible = true;
        OverlayRoot.UpdateLayout();
        PaletteCard.RenderTransformOrigin = OriginFor(PaletteCard, SearchButton);
        if (PaletteCard.Opacity < 0.01)
        {
            SpringAnimator.Set(PaletteScale, ScaleTransform.ScaleXProperty, OverlayScale);
            SpringAnimator.Set(PaletteScale, ScaleTransform.ScaleYProperty, OverlayScale);
        }

        SpringAnimator.Animate(PaletteCard, OpacityProperty, 1, SpringSpec.Quick);
        SpringAnimator.Animate(PaletteScale, ScaleTransform.ScaleXProperty, 1, SpringSpec.Standard, precision: 0.001);
        SpringAnimator.Animate(PaletteScale, ScaleTransform.ScaleYProperty, 1, SpringSpec.Standard, precision: 0.001);
        PaletteInput.Focus();
        PaletteInput.SelectAll();
    }

    private void HidePalette()
    {
        if (!_paletteVisible)
        {
            return;
        }

        _paletteVisible = false;
        PaletteCard.IsHitTestVisible = false;
        SpringAnimator.Animate(PaletteCard, OpacityProperty, 0, SpringSpec.Quick, completed: () => PaletteCard.Visibility = Visibility.Collapsed);
        SpringAnimator.Animate(PaletteScale, ScaleTransform.ScaleXProperty, OverlayScale, SpringSpec.Standard, precision: 0.001);
        SpringAnimator.Animate(PaletteScale, ScaleTransform.ScaleYProperty, OverlayScale, SpringSpec.Standard, precision: 0.001);
        HideOverlayIfIdle();
    }

    private void PaletteInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _viewModel.Palette.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.Palette.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.Palette.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.Palette.Close();
                e.Handled = true;
                break;
        }
    }

    private void PaletteResults_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(PaletteResults, source) is ListBoxItem { DataContext: SearchItem item })
        {
            _viewModel.Palette.Execute(item);
        }
    }

    private void OnDialogOpened(object? sender, EventArgs e)
    {
        if (_viewModel.Dialogs.Current is not { } request)
        {
            return;
        }

        if (_viewModel.Palette.IsOpen)
        {
            _viewModel.Palette.Close();
        }

        _dialogVisible = true;
        RememberFocus();
        ShowOverlay();
        DialogConfirm.SetResourceReference(StyleProperty, request.IsDestructive ? "Button.Destructive" : "Button.Primary");
        DialogCard.Visibility = Visibility.Visible;
        DialogCard.IsHitTestVisible = true;
        OverlayRoot.UpdateLayout();
        var anchor = _viewModel.Dialogs.Anchor;
        DialogCard.RenderTransformOrigin = anchor is { IsVisible: true } ? OriginFor(DialogCard, anchor) : new Point(0.5, 0.5);
        if (DialogCard.Opacity < 0.01)
        {
            SpringAnimator.Set(DialogScale, ScaleTransform.ScaleXProperty, DialogStartScale);
            SpringAnimator.Set(DialogScale, ScaleTransform.ScaleYProperty, DialogStartScale);
        }

        SpringAnimator.Animate(DialogCard, OpacityProperty, 1, SpringSpec.Quick);
        SpringAnimator.Animate(DialogScale, ScaleTransform.ScaleXProperty, 1, SpringSpec.Standard, precision: 0.001);
        SpringAnimator.Animate(DialogScale, ScaleTransform.ScaleYProperty, 1, SpringSpec.Standard, precision: 0.001);
        var initial = request.IsDestructive && request.HasCancel ? DialogCancel : DialogConfirm;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => initial.Focus());
    }

    private void OnDialogClosing(object? sender, EventArgs e)
    {
        _dialogVisible = false;
        DialogCard.IsHitTestVisible = false;
        SpringAnimator.Animate(DialogCard, OpacityProperty, 0, SpringSpec.Quick, completed: () =>
        {
            DialogCard.Visibility = Visibility.Collapsed;
            _viewModel.Dialogs.ClearAfterClose();
        });
        SpringAnimator.Animate(DialogScale, ScaleTransform.ScaleXProperty, DialogStartScale, SpringSpec.Standard, precision: 0.001);
        SpringAnimator.Animate(DialogScale, ScaleTransform.ScaleYProperty, DialogStartScale, SpringSpec.Standard, precision: 0.001);
        HideOverlayIfIdle();
    }

    private void ShowOverlay()
    {
        OverlayRoot.Visibility = Visibility.Visible;
        SpringAnimator.Animate(Scrim, OpacityProperty, 1, SpringSpec.Quick);
    }

    private void HideOverlayIfIdle()
    {
        if (_paletteVisible || _dialogVisible)
        {
            return;
        }

        SpringAnimator.Animate(Scrim, OpacityProperty, 0, SpringSpec.Quick, completed: () =>
        {
            if (!_paletteVisible && !_dialogVisible)
            {
                OverlayRoot.Visibility = Visibility.Collapsed;
            }
        });
        RestoreFocus();
    }

    private void RememberFocus() => _focusBeforeOverlay ??= Keyboard.FocusedElement;

    private void RestoreFocus()
    {
        var target = _focusBeforeOverlay;
        _focusBeforeOverlay = null;
        if (target is UIElement { IsVisible: true } element)
        {
            element.Focus();
        }
    }

    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelTopOverlay();
        e.Handled = true;
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_dialogVisible || _paletteVisible))
        {
            CancelTopOverlay();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _gate.IsBusy)
        {
            _gate.RequestStop();
            e.Handled = true;
        }
        else if (_dialogVisible && e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
        }
    }

    private void CancelTopOverlay()
    {
        if (_dialogVisible)
        {
            var dialogs = _viewModel.Dialogs;
            if (dialogs.Current is { HasCancel: true })
            {
                dialogs.CancelCommand.Execute(null);
            }
            else
            {
                dialogs.ConfirmCommand.Execute(null);
            }
        }
        else if (_paletteVisible)
        {
            _viewModel.Palette.Close();
        }
    }

    private static Point OriginFor(FrameworkElement card, FrameworkElement anchor)
    {
        if (card.ActualWidth < 1 || card.ActualHeight < 1 || PresentationSource.FromVisual(anchor) is null)
        {
            return new Point(0.5, 0.5);
        }

        var center = anchor.TransformToVisual(card).Transform(new Point(anchor.ActualWidth / 2, anchor.ActualHeight / 2));
        return new Point(center.X / card.ActualWidth, center.Y / card.ActualHeight);
    }

    private void OnThemeChanging(object? sender, ThemeChangingEventArgs e)
    {
        if (!IsLoaded || Root.ActualWidth < 1 || Root.ActualHeight < 1)
        {
            e.Apply();
            return;
        }

        ThemeSnapshot.Visibility = Visibility.Collapsed;
        var snapshot = WindowInterop.Render(Root);
        ThemeSnapshot.Source = snapshot;
        ThemeSnapshot.Width = Root.ActualWidth;
        ThemeSnapshot.Height = Root.ActualHeight;
        ThemeSnapshot.Visibility = Visibility.Visible;
        SpringAnimator.Set(ThemeSnapshot, OpacityProperty, 1);
        e.Apply();
        SpringAnimator.Animate(ThemeSnapshot, OpacityProperty, 0, SpringSpec.Gentle, completed: () =>
        {
            ThemeSnapshot.Visibility = Visibility.Collapsed;
            ThemeSnapshot.Source = null;
        });
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyBackdrop();

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Navigation.Navigated -= OnNavigated;
        _viewModel.Palette.PropertyChanged -= OnPalettePropertyChanged;
        _viewModel.Dialogs.Opened -= OnDialogOpened;
        _viewModel.Dialogs.Closing -= OnDialogClosing;
        _theme.Changing -= OnThemeChanging;
        _theme.Changed -= OnThemeChanged;
        _source?.RemoveHook(WndProc);
    }

    private sealed class PillState
    {
        public double TargetY { get; set; } = double.NaN;

        public double TargetOpacity { get; set; } = -1;
    }
}
