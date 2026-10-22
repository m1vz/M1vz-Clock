using System.Windows;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.DesignSystem.Controls;

public sealed class ProgressTrack : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(ProgressTrack), new PropertyMetadata(0.0, OnFractionChanged));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(ProgressTrack), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(ProgressTrack), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty DisplayFractionProperty = DependencyProperty.Register(
        "DisplayFraction", typeof(double), typeof(ProgressTrack), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double DefaultHeight = 6;

    public ProgressTrack()
    {
        Loaded += (_, _) => SpringAnimator.Set(this, DisplayFractionProperty, Clamp(Fraction));
    }

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
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

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, DefaultHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var radius = ActualHeight / 2;
        drawingContext.DrawRoundedRectangle(Track, null, new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
        var fraction = Clamp((double)GetValue(DisplayFractionProperty));
        var width = Math.Max(fraction * ActualWidth, fraction > 0 ? ActualHeight : 0);
        if (width > 0)
        {
            drawingContext.DrawRoundedRectangle(Foreground, null, new Rect(0, 0, Math.Min(width, ActualWidth), ActualHeight), radius, radius);
        }
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);

    private static void OnFractionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var track = (ProgressTrack)d;
        var target = Clamp((double)e.NewValue);
        if (track.IsLoaded)
        {
            SpringAnimator.Animate(track, DisplayFractionProperty, target, SpringSpec.Gentle, precision: 0.0005);
        }
        else
        {
            SpringAnimator.Set(track, DisplayFractionProperty, target);
        }
    }
}
