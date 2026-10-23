using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace M1vClock.App.DesignSystem.Controls;

public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<ChartPoint>), typeof(HistoryChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkersProperty = DependencyProperty.Register(
        nameof(Markers), typeof(IReadOnlyList<double>), typeof(HistoryChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GuidesProperty = DependencyProperty.Register(
        nameof(Guides), typeof(IReadOnlyList<double>), typeof(HistoryChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(HistoryChart), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(HistoryChart), new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double Gutter = 36;
    private const double VerticalPadding = 10;
    private const double MarkerHeight = 6;
    private const double DefaultHeight = 180;

    public HistoryChart()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public IReadOnlyList<ChartPoint>? Points
    {
        get => (IReadOnlyList<ChartPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IReadOnlyList<double>? Markers
    {
        get => (IReadOnlyList<double>?)GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public IReadOnlyList<double>? Guides
    {
        get => (IReadOnlyList<double>?)GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, double.IsInfinity(availableSize.Height) ? DefaultHeight : availableSize.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var plot = new Rect(Gutter, VerticalPadding, Math.Max(0, ActualWidth - Gutter), Math.Max(0, ActualHeight - (2 * VerticalPadding)));
        if (plot.Width < 20 || plot.Height < 20 || Maximum <= Minimum)
        {
            return;
        }

        var separator = Resource("Brush.Separator");
        var gridPen = new Pen(separator, 1);
        gridPen.Freeze();
        foreach (var guide in Guides ?? [Minimum, (Minimum + Maximum) / 2, Maximum])
        {
            if (guide < Minimum || guide > Maximum)
            {
                continue;
            }

            var y = Math.Round(ToY(guide, plot)) + 0.5;
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            DrawLabel(drawingContext, guide.ToString("0.##", CultureInfo.CurrentCulture), plot.Left - 8, y);
        }

        var points = Points;
        if (points is null || points.Count < 2)
        {
            return;
        }

        var first = points[0].Time;
        var span = Math.Max(1e-9, points[^1].Time - first);
        drawingContext.PushClip(new RectangleGeometry(plot));
        DrawLine(drawingContext, points, plot, first, span);
        DrawMarkers(drawingContext, plot, first, span);
        drawingContext.Pop();
    }

    private void DrawLine(DrawingContext context, IReadOnlyList<ChartPoint> points, Rect plot, double first, double span)
    {
        var line = new StreamGeometry();
        using (var figure = line.Open())
        {
            figure.BeginFigure(Map(points[0], plot, first, span), false, false);
            for (var i = 1; i < points.Count; i++)
            {
                figure.LineTo(Map(points[i], plot, first, span), true, true);
            }
        }

        line.Freeze();
        var area = new StreamGeometry();
        using (var figure = area.Open())
        {
            figure.BeginFigure(new Point(plot.Left, plot.Bottom), true, true);
            for (var i = 0; i < points.Count; i++)
            {
                figure.LineTo(Map(points[i], plot, first, span), false, false);
            }

            figure.LineTo(new Point(plot.Right, plot.Bottom), false, false);
        }

        area.Freeze();
        context.DrawGeometry(Resource("Brush.ChartFill"), null, area);
        var pen = new Pen(Resource("Brush.AccentForeground"), 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        context.DrawGeometry(null, pen, line);
    }

    private void DrawMarkers(DrawingContext context, Rect plot, double first, double span)
    {
        if (Markers is not { Count: > 0 } markers)
        {
            return;
        }

        var pen = new Pen(Resource("Brush.Critical"), 2);
        pen.Freeze();
        foreach (var marker in markers)
        {
            var x = plot.Left + (((marker - first) / span) * plot.Width);
            if (x < plot.Left || x > plot.Right)
            {
                continue;
            }

            context.DrawLine(pen, new Point(x, plot.Bottom - MarkerHeight), new Point(x, plot.Bottom));
        }
    }

    private double ToY(double value, Rect plot) => plot.Bottom - (((value - Minimum) / (Maximum - Minimum)) * plot.Height);

    private Point Map(ChartPoint point, Rect plot, double first, double span) =>
        new(plot.Left + (((point.Time - first) / span) * plot.Width), ToY(Math.Clamp(point.Value, Minimum, Maximum), plot));

    private void DrawLabel(DrawingContext context, string text, double right, double centerY)
    {
        var family = TryFindResource("Font.Text") as FontFamily ?? new FontFamily("Segoe UI");
        var size = TryFindResource("Size.Caption") as double? ?? 11;
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Resource("Brush.TextTertiary"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(label, new Point(right - label.Width, centerY - (label.Height / 2)));
    }

    private Brush Resource(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}
