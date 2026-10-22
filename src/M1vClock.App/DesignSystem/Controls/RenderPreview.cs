using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace M1vClock.App.DesignSystem.Controls;

public readonly record struct PreviewRegion(int X, int Y, int Width, int Height);

public interface IPreviewSource
{
    int PixelWidth { get; }

    int PixelHeight { get; }

    void CopyPixels(Span<int> destination);

    IReadOnlyList<PreviewRegion> ActiveRegions();
}

public sealed class RenderPreview : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(IPreviewSource), typeof(RenderPreview), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnSourceChanged));

    private const double CornerRadius = 10;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);
    private static readonly Pen RegionPen = CreateRegionPen();

    private readonly DispatcherTimer _timer;
    private WriteableBitmap? _bitmap;
    private int[] _pixels = [];
    private IReadOnlyList<PreviewRegion> _regions = [];

    public RenderPreview()
    {
        _timer = new DispatcherTimer(Interval, DispatcherPriority.Render, (_, _) => Refresh(), Dispatcher);
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    public IPreviewSource? Source
    {
        get => (IPreviewSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var source = Source;
        if (source is null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return new Size(0, 0);
        }

        var width = double.IsInfinity(availableSize.Width) ? source.PixelWidth : availableSize.Width;
        var height = width * source.PixelHeight / source.PixelWidth;
        if (!double.IsInfinity(availableSize.Height) && height > availableSize.Height)
        {
            height = availableSize.Height;
            width = height * source.PixelWidth / source.PixelHeight;
        }

        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (_bitmap is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.PushClip(new RectangleGeometry(bounds, CornerRadius, CornerRadius));
        drawingContext.DrawImage(_bitmap, bounds);
        var scaleX = ActualWidth / _bitmap.PixelWidth;
        var scaleY = ActualHeight / _bitmap.PixelHeight;
        foreach (var region in _regions)
        {
            drawingContext.DrawRectangle(null, RegionPen, new Rect(region.X * scaleX, region.Y * scaleY, region.Width * scaleX, region.Height * scaleY));
        }

        drawingContext.Pop();
    }

    private static Pen CreateRegionPen()
    {
        var pen = new Pen(Brushes.White, 1.5);
        pen.Freeze();
        return pen;
    }

    private static void OnSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var preview = (RenderPreview)sender;
        preview._bitmap = null;
        preview._regions = [];
        preview.Refresh();
    }

    private void Refresh()
    {
        var source = Source;
        if (source is null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return;
        }

        if (_bitmap is null || _bitmap.PixelWidth != source.PixelWidth || _bitmap.PixelHeight != source.PixelHeight)
        {
            _bitmap = new WriteableBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null);
            _pixels = new int[source.PixelWidth * source.PixelHeight];
        }

        source.CopyPixels(_pixels);
        _bitmap.WritePixels(new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight), _pixels, source.PixelWidth * 4, 0);
        _regions = source.ActiveRegions();
        InvalidateVisual();
    }
}
