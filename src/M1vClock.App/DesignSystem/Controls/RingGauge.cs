using System.Windows;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.DesignSystem.Controls;

public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(RingGauge), new PropertyMetadata(0.0, OnFractionChanged));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(MetricState), typeof(RingGauge), new FrameworkPropertyMetadata(MetricState.Neutral, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty DisplayFractionProperty = DependencyProperty.Register(
        "DisplayFraction", typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double FullTurn = 0.9999;

    public RingGauge()
    {
        Loaded += (_, _) => SpringAnimator.Set(this, DisplayFractionProperty, Clamp(Fraction));
    }

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public MetricState State
    {
        get => (MetricState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(double.IsInfinity(availableSize.Width) ? 84 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 84 : availableSize.Height);
        return new Size(side, side);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var side = Math.Min(ActualWidth, ActualHeight);
        var thickness = Math.Min(RingThickness, side / 2);
        var radius = (side - thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        drawingContext.DrawEllipse(null, new Pen(Track, thickness), center, radius, radius);
        var fraction = Math.Clamp((double)GetValue(DisplayFractionProperty), 0, FullTurn);
        if (fraction <= 0)
        {
            return;
        }

        var angle = fraction * 2 * Math.PI;
        var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(center.X, center.Y - radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        var pen = new Pen(Foreground, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (RingGauge)d;
        var target = Clamp((double)e.NewValue);
        if (ring.IsLoaded)
        {
            SpringAnimator.Animate(ring, DisplayFractionProperty, target, SpringSpec.Gentle, precision: 0.0005);
        }
        else
        {
            SpringAnimator.Set(ring, DisplayFractionProperty, target);
        }
    }
}
