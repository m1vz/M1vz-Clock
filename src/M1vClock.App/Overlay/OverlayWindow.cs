using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using M1vClock.Games.Overlay;

namespace M1vClock.App.Overlay;

public sealed class OverlayWindow : Window
{
    private readonly OverlayPanel _panel = new();
    private IntPtr _handle;

    public OverlayWindow()
    {
        Title = "m1vClock overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = _panel;
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            OverlayNative.MakeClickThrough(_handle);
        };
        SizeChanged += (_, _) => KeepOnTop();
    }

    public OverlayCorner Corner { get; set; } = OverlayCorner.TopLeft;

    public IntPtr GameWindow { get; set; }

    public void Apply(OverlayView view, double scale)
    {
        ArgumentNullException.ThrowIfNull(view);
        _panel.DataContext = view;
        _panel.SetScale(Math.Clamp(scale, OverlaySettings.MinimumScale, OverlaySettings.MaximumScale));
        KeepOnTop();
    }

    public void KeepOnTop()
    {
        if (_handle == IntPtr.Zero || !IsVisible)
        {
            return;
        }

        var monitor = OverlayNative.MonitorOf(GameWindow);
        if (!OverlayNative.TryMonitorBounds(monitor, out var screen) || !OverlayNative.TryWindowBounds(_handle, out var own))
        {
            return;
        }

        var (x, y) = OverlayPlacement.Position(Corner, screen.Left, screen.Top, screen.Right, screen.Bottom, own.Right - own.Left, own.Bottom - own.Top, VisualTreeHelper.GetDpi(this).DpiScaleX);
        OverlayNative.PlaceOnTop(_handle, x, y);
    }
}

public static class OverlayPlacement
{
    public const int Margin = 12;

    public static (int X, int Y) Position(OverlayCorner corner, int left, int top, int right, int bottom, int width, int height, double dpiScale)
    {
        var margin = (int)Math.Round(Margin * dpiScale);
        var x = corner is OverlayCorner.TopRight or OverlayCorner.BottomRight ? right - width - margin : left + margin;
        var y = corner is OverlayCorner.BottomLeft or OverlayCorner.BottomRight ? bottom - height - margin : top + margin;
        return (x, y);
    }
}
