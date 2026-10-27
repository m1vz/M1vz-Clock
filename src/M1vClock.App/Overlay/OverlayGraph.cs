using System.Windows;
using System.Windows.Media;

namespace M1vClock.App.Overlay;

public sealed class OverlayGraph : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(OverlayGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(OverlayGraph), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen Baseline = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1));

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        drawingContext.DrawLine(Baseline, new Point(0, height - 0.5), new Point(width, height - 0.5));
        if (Values is not { Count: >= 2 } values)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var step = width / (values.Count - 1);
            context.BeginFigure(new Point(0, Y(values[0], height)), false, false);
            for (var i = 1; i < values.Count; i++)
            {
                context.LineTo(new Point(i * step, Y(values[i], height)), true, false);
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(Stroke, 1), geometry);
    }

    private static double Y(double value, double height) => height - 1 - ((height - 2) * Math.Clamp(value, 0, 1));

    private static Pen Freeze(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
