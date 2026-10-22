using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;
using M1vClock.App.Localization;

namespace M1vClock.App.DesignSystem.Controls;

public readonly record struct ChartPoint(double Time, double Value);

public static class ChartClock
{
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

public sealed class TimeSeries(string label, TimeSpan retention)
{
    private readonly List<ChartPoint> _points = [];

    public event EventHandler? Updated;

    public string Label { get; } = label;

    public IReadOnlyList<ChartPoint> Points => _points;

    public double? Latest => _points.Count == 0 ? null : _points[^1].Value;

    public void Add(double value) => Add(ChartClock.Now, value);

    public void Add(double time, double value)
    {
        _points.Add(new ChartPoint(time, value));
        var cutoff = time - retention.TotalSeconds - 2;
        var remove = 0;
        while (remove < _points.Count && _points[remove].Time < cutoff)
        {
            remove++;
        }

        if (remove > 0)
        {
            _points.RemoveRange(0, remove);
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _points.Clear();
        Updated?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class LiveChart : FrameworkElement
{
    public static readonly DependencyProperty PrimaryProperty = DependencyProperty.Register(
        nameof(Primary), typeof(TimeSeries), typeof(LiveChart), new PropertyMetadata(null, OnSeriesChanged));

    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        nameof(Secondary), typeof(TimeSeries), typeof(LiveChart), new PropertyMetadata(null, OnSeriesChanged));

    public static readonly DependencyProperty WindowSecondsProperty = DependencyProperty.Register(
        nameof(WindowSeconds), typeof(double), typeof(LiveChart), new FrameworkPropertyMetadata(60.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(LiveChart), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
        nameof(ValueFormat), typeof(string), typeof(LiveChart), new FrameworkPropertyMetadata("0", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumRangeProperty = DependencyProperty.Register(
        nameof(MinimumRange), typeof(double), typeof(LiveChart), new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FloorProperty = DependencyProperty.Register(
        nameof(Floor), typeof(double), typeof(LiveChart), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double Gutter = 48;
    private const double VerticalPadding = 8;
    private const double EntryDelaySeconds = 1;

    private readonly VisualCollection _children;
    private readonly TextBlock[] _axisLabels = new TextBlock[3];
    private readonly Border _bubble;
    private readonly TextBlock _bubbleText;
    private double _top = double.NaN;
    private double _bottom = double.NaN;
    private double _topVelocity;
    private double _bottomVelocity;
    private double _lastFrame;
    private double? _hoverX;
    private Point _bubbleAnchor;
    private bool _hooked;

    public LiveChart()
    {
        _children = new VisualCollection(this);
        for (var i = 0; i < _axisLabels.Length; i++)
        {
            _axisLabels[i] = CreateLabel("Brush.TextTertiary", "Size.Caption");
            _children.Add(_axisLabels[i]);
        }

        _bubbleText = CreateLabel("Brush.TextPrimary", "Size.Subhead");
        _bubble = new Border
        {
            Child = _bubbleText,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _bubble.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceElevated");
        _bubble.SetResourceReference(Border.BorderBrushProperty, "Brush.Separator");
        _children.Add(_bubble);

        ClipToBounds = true;
        Loaded += (_, _) => Hook();
        Unloaded += (_, _) => Unhook();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Hook();
            }
            else
            {
                Unhook();
            }
        };
    }

    public TimeSeries? Primary
    {
        get => (TimeSeries?)GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    public TimeSeries? Secondary
    {
        get => (TimeSeries?)GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    public double WindowSeconds
    {
        get => (double)GetValue(WindowSecondsProperty);
        set => SetValue(WindowSecondsProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    public double MinimumRange
    {
        get => (double)GetValue(MinimumRangeProperty);
        set => SetValue(MinimumRangeProperty, value);
    }

    public double Floor
    {
        get => (double)GetValue(FloorProperty);
        set => SetValue(FloorProperty, value);
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in _children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var plotTop = VerticalPadding;
        var plotHeight = Math.Max(1, finalSize.Height - 2 * VerticalPadding);
        for (var i = 0; i < _axisLabels.Length; i++)
        {
            var label = _axisLabels[i];
            var y = plotTop + plotHeight * (1 - i / 2.0) - label.DesiredSize.Height / 2;
            label.Arrange(new Rect(new Point(Gutter - 8 - label.DesiredSize.Width, Math.Clamp(y, 0, finalSize.Height - label.DesiredSize.Height)), label.DesiredSize));
        }

        if (_bubble.Visibility == Visibility.Visible)
        {
            var size = _bubble.DesiredSize;
            var x = Math.Clamp(_bubbleAnchor.X + 12, Gutter, Math.Max(Gutter, finalSize.Width - size.Width));
            if (x + size.Width > finalSize.Width - 4)
            {
                x = Math.Max(Gutter, _bubbleAnchor.X - 12 - size.Width);
            }

            var y = Math.Clamp(_bubbleAnchor.Y - size.Height - 8, 0, Math.Max(0, finalSize.Height - size.Height));
            _bubble.Arrange(new Rect(new Point(x, y), size));
        }
        else
        {
            _bubble.Arrange(new Rect(0, 0, 0, 0));
        }

        return finalSize;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var x = e.GetPosition(this).X;
        _hoverX = x >= Gutter ? x : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverX = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= Gutter + 10 || height <= 2 * VerticalPadding + 10)
        {
            return;
        }

        var plot = new Rect(Gutter, VerticalPadding, width - Gutter, height - 2 * VerticalPadding);
        var now = ChartClock.Now - EntryDelaySeconds;
        var window = Math.Max(5, WindowSeconds);
        var start = now - window;
        UpdateRange(start, now);

        var separator = Resource("Brush.Separator");
        var gridPen = new Pen(separator, 1);
        gridPen.Freeze();
        for (var i = 0; i <= 2; i++)
        {
            var y = Math.Round(plot.Top + plot.Height * (i / 2.0)) + 0.5;
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        UpdateAxisLabels();

        drawingContext.PushClip(new RectangleGeometry(plot));
        if (Secondary is { } secondary)
        {
            DrawSeries(drawingContext, secondary, plot, start, window, Resource("Brush.TextTertiary"), 1.5, null);
        }

        if (Primary is { } primary)
        {
            DrawSeries(drawingContext, primary, plot, start, window, Resource("Brush.AccentForeground"), 2, Resource("Brush.ChartFill"));
        }

        DrawHover(drawingContext, plot, start, window, separator);
        drawingContext.Pop();
    }

    private void DrawSeries(DrawingContext context, TimeSeries series, Rect plot, double start, double window, Brush stroke, double thickness, Brush? fill)
    {
        var points = series.Points;
        var first = FirstVisible(points, start);
        if (points.Count - first < 2)
        {
            return;
        }

        var line = new StreamGeometry();
        using (var figure = line.Open())
        {
            figure.BeginFigure(Map(points[first], plot, start, window), false, false);
            for (var i = first + 1; i < points.Count; i++)
            {
                figure.LineTo(Map(points[i], plot, start, window), true, true);
            }
        }

        line.Freeze();
        if (fill is not null)
        {
            var area = new StreamGeometry();
            using (var figure = area.Open())
            {
                var firstPoint = Map(points[first], plot, start, window);
                figure.BeginFigure(new Point(firstPoint.X, plot.Bottom), true, true);
                for (var i = first; i < points.Count; i++)
                {
                    figure.LineTo(Map(points[i], plot, start, window), false, false);
                }

                figure.LineTo(new Point(Map(points[^1], plot, start, window).X, plot.Bottom), false, false);
            }

            area.Freeze();
            context.DrawGeometry(fill, null, area);
        }

        var pen = new Pen(stroke, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        context.DrawGeometry(null, pen, line);
    }

    private void DrawHover(DrawingContext context, Rect plot, double start, double window, Brush separator)
    {
        if (_hoverX is not double hoverX || Primary is not { } primary || primary.Points.Count == 0)
        {
            SetBubble(null);
            return;
        }

        var time = start + (hoverX - plot.Left) / plot.Width * window;
        var nearest = Nearest(primary.Points, time);
        if (nearest is not ChartPoint point || Math.Abs(point.Time - time) > window / 20)
        {
            SetBubble(null);
            return;
        }

        var anchor = Map(point, plot, start, window);
        var linePen = new Pen(separator, 1);
        linePen.Freeze();
        context.DrawLine(linePen, new Point(Math.Round(anchor.X) + 0.5, plot.Top), new Point(Math.Round(anchor.X) + 0.5, plot.Bottom));
        var dotPen = new Pen(Resource("Brush.AccentForeground"), 2);
        dotPen.Freeze();
        context.DrawEllipse(Resource("Brush.Surface"), dotPen, anchor, 4, 4);

        var culture = Loc.Culture;
        var text = Format(point.Value);
        if (Secondary is { } secondary && Nearest(secondary.Points, point.Time) is ChartPoint other)
        {
            text = $"{primary.Label} {text} · {secondary.Label} {Format(other.Value)}";
        }

        var ago = Math.Max(0, ChartClock.Now - point.Time);
        text += " · " + Loc.Format("Chart.SecondsAgo", Math.Round(ago));
        _bubbleAnchor = anchor;
        SetBubble(text);
    }

    private void SetBubble(string? text)
    {
        var visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        var changed = false;
        if (text is not null && _bubbleText.Text != text)
        {
            _bubbleText.Text = text;
            changed = true;
        }

        if (_bubble.Visibility != visibility)
        {
            _bubble.Visibility = visibility;
            changed = true;
        }

        if (changed)
        {
            InvalidateMeasure();
        }

        if (visibility == Visibility.Visible)
        {
            InvalidateArrange();
        }
    }

    private void UpdateRange(double start, double now)
    {
        var (min, max) = (double.PositiveInfinity, double.NegativeInfinity);
        foreach (var series in new[] { Primary, Secondary })
        {
            if (series is null)
            {
                continue;
            }

            foreach (var point in series.Points)
            {
                if (point.Time < start - 1)
                {
                    continue;
                }

                min = Math.Min(min, point.Value);
                max = Math.Max(max, point.Value);
            }
        }

        if (double.IsInfinity(min))
        {
            (min, max) = (0, MinimumRange);
        }

        var bottom = double.IsNaN(Floor) ? NiceFloor(min - (max - min) * 0.1) : Floor;
        var step = NiceStep(Math.Max(MinimumRange, (max - bottom) * 1.1) / 2);
        var top = bottom + 2 * step;
        while (top < max)
        {
            step = NiceStep(step * 1.01);
            top = bottom + 2 * step;
        }

        var seconds = _lastFrame == 0 ? 1 : Math.Clamp(now - _lastFrame, 0, 0.05);
        _lastFrame = now;
        if (double.IsNaN(_top) || SpringAnimator.ReduceMotion)
        {
            (_top, _bottom, _topVelocity, _bottomVelocity) = (top, bottom, 0, 0);
            return;
        }

        (_top, _topVelocity) = SpringMath.Step(_top, _topVelocity, top, SpringSpec.Gentle, seconds);
        (_bottom, _bottomVelocity) = SpringMath.Step(_bottom, _bottomVelocity, bottom, SpringSpec.Gentle, seconds);
    }

    private void UpdateAxisLabels()
    {
        for (var i = 0; i < _axisLabels.Length; i++)
        {
            var value = _bottom + (_top - _bottom) * i / 2.0;
            var text = Format(value);
            if (_axisLabels[i].Text != text)
            {
                _axisLabels[i].Text = text;
                InvalidateMeasure();
            }
        }
    }

    private string Format(double value)
    {
        var number = value.ToString(ValueFormat, Loc.Culture);
        return string.IsNullOrEmpty(Unit) ? number : $"{number} {Unit}";
    }

    private Point Map(ChartPoint point, Rect plot, double start, double window)
    {
        var range = Math.Max(1e-6, _top - _bottom);
        var x = plot.Left + (point.Time - start) / window * plot.Width;
        var y = plot.Bottom - (point.Value - _bottom) / range * plot.Height;
        return new Point(x, y);
    }

    private static int FirstVisible(IReadOnlyList<ChartPoint> points, double start)
    {
        var index = 0;
        while (index < points.Count - 1 && points[index + 1].Time < start)
        {
            index++;
        }

        return index;
    }

    private static ChartPoint? Nearest(IReadOnlyList<ChartPoint> points, double time)
    {
        ChartPoint? best = null;
        var distance = double.MaxValue;
        foreach (var point in points)
        {
            var d = Math.Abs(point.Time - time);
            if (d < distance)
            {
                distance = d;
                best = point;
            }
        }

        return best;
    }

    public static double NiceStep(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw))
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var factor in new[] { 1.0, 2.0, 2.5, 5.0, 10.0 })
        {
            if (factor * magnitude >= raw)
            {
                return factor * magnitude;
            }
        }

        return 10 * magnitude;
    }

    private static double NiceFloor(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var step = NiceStep(value / 4);
        return Math.Floor(value / step) * step;
    }

    private Brush Resource(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private static TextBlock CreateLabel(string brushKey, string sizeKey)
    {
        var label = new TextBlock { IsHitTestVisible = false };
        label.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        label.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        label.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Text");
        Typography.SetNumeralAlignment(label, FontNumeralAlignment.Tabular);
        return label;
    }

    private void Hook()
    {
        if (_hooked || !IsLoaded || !IsVisible)
        {
            return;
        }

        _hooked = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Unhook()
    {
        if (!_hooked)
        {
            return;
        }

        _hooked = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (!SpringAnimator.ReduceMotion)
        {
            InvalidateVisual();
        }
    }

    private void OnSeriesUpdated(object? sender, EventArgs e)
    {
        if (SpringAnimator.ReduceMotion)
        {
            InvalidateVisual();
        }

        var primary = Primary?.Latest;
        if (primary is double value)
        {
            AutomationProperties.SetHelpText(this, Format(value));
        }
    }

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (LiveChart)d;
        if (e.OldValue is TimeSeries old)
        {
            old.Updated -= chart.OnSeriesUpdated;
        }

        if (e.NewValue is TimeSeries series)
        {
            series.Updated += chart.OnSeriesUpdated;
        }

        chart.InvalidateVisual();
    }
}
