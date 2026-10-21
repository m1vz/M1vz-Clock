using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.DesignSystem.Controls;

public sealed class MomentumTrack : Border
{
    private const double PuckSize = 40;
    private const double Inset = 12;
    private const double MarkerSize = 6;
    private const int Stops = 3;

    private readonly Grid _layer = new();
    private readonly Border _puck;
    private readonly TranslateTransform _offset = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Ellipse[] _markers = new Ellipse[Stops];
    private readonly VelocityTracker _velocity = new();
    private bool _dragging;
    private double _grab;
    private int _index = 1;

    public MomentumTrack()
    {
        Height = 64;
        CornerRadius = new CornerRadius(12);
        Focusable = true;
        FocusVisualStyle = null;
        SetResourceReference(BackgroundProperty, "Brush.FillPrimary");
        Child = _layer;

        for (var i = 0; i < Stops; i++)
        {
            var marker = new Ellipse
            {
                Width = MarkerSize,
                Height = MarkerSize,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            marker.SetResourceReference(Shape.FillProperty, "Brush.TextTertiary");
            _markers[i] = marker;
            _layer.Children.Add(marker);
        }

        var transforms = new TransformGroup();
        transforms.Children.Add(_scale);
        transforms.Children.Add(_offset);
        _puck = new Border
        {
            Width = PuckSize,
            Height = PuckSize,
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = transforms,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Cursor = Cursors.Hand,
        };
        _puck.SetResourceReference(BackgroundProperty, "Brush.Accent");
        _layer.Children.Add(_puck);

        SizeChanged += (_, _) => Layout();
        _puck.MouseLeftButtonDown += OnPuckDown;
        _puck.MouseMove += OnPuckMove;
        _puck.MouseLeftButtonUp += OnPuckUp;
        _puck.LostMouseCapture += (_, _) =>
        {
            if (_dragging)
            {
                Release(0);
            }
        };
        GotKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, "Brush.Accent");
        LostKeyboardFocus += (_, _) => BorderBrush = null;
        BorderThickness = new Thickness(2);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        var next = e.Key switch
        {
            Key.Left => _index - 1,
            Key.Right => _index + 1,
            _ => _index,
        };
        if (next != _index && next is >= 0 and < Stops)
        {
            _index = next;
            SpringAnimator.Animate(_offset, TranslateTransform.XProperty, SnapX(_index), SpringSpec.Standard, precision: 0.1);
            e.Handled = true;
        }
    }

    private double Usable => Math.Max(0, ActualWidth - BorderThickness.Left - BorderThickness.Right - 2 * Inset - PuckSize);

    private double SnapX(int index) => Inset + index * Usable / (Stops - 1);

    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private void Layout()
    {
        for (var i = 0; i < Stops; i++)
        {
            _markers[i].Margin = new Thickness(SnapX(i) + (PuckSize - MarkerSize) / 2, 0, 0, 0);
        }

        if (!_dragging && !SpringAnimator.IsAnimating(_offset, TranslateTransform.XProperty))
        {
            SpringAnimator.Set(_offset, TranslateTransform.XProperty, SnapX(_index));
        }
    }

    private void OnPuckDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var current = _offset.X;
        SpringAnimator.Set(_offset, TranslateTransform.XProperty, current);
        _grab = e.GetPosition(_layer).X - current;
        _dragging = true;
        _velocity.Reset();
        _velocity.Add(Now, current);
        _puck.CaptureMouse();
        SpringAnimator.Animate(_scale, ScaleTransform.ScaleXProperty, 0.97, SpringSpec.Press, precision: 0.001);
        SpringAnimator.Animate(_scale, ScaleTransform.ScaleYProperty, 0.97, SpringSpec.Press, precision: 0.001);
        e.Handled = true;
    }

    private void OnPuckMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var raw = e.GetPosition(_layer).X - _grab;
        var min = SnapX(0);
        var max = SnapX(Stops - 1);
        var x = raw < min
            ? min + SpringMath.RubberBand(raw - min, PuckSize)
            : raw > max ? max + SpringMath.RubberBand(raw - max, PuckSize) : raw;
        SpringAnimator.Set(_offset, TranslateTransform.XProperty, x);
        _velocity.Add(Now, x);
    }

    private void OnPuckUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var velocity = _velocity.Velocity();
        _dragging = false;
        _puck.ReleaseMouseCapture();
        Release(velocity);
        e.Handled = true;
    }

    private void Release(double velocity)
    {
        _dragging = false;
        var projected = _offset.X + SpringMath.Project(velocity);
        var best = 0;
        for (var i = 1; i < Stops; i++)
        {
            if (Math.Abs(SnapX(i) - projected) < Math.Abs(SnapX(best) - projected))
            {
                best = i;
            }
        }

        _index = best;
        SpringAnimator.Animate(_offset, TranslateTransform.XProperty, SnapX(best), SpringSpec.Momentum, initialVelocity: velocity, precision: 0.1);
        SpringAnimator.Animate(_scale, ScaleTransform.ScaleXProperty, 1, SpringSpec.Press, precision: 0.001);
        SpringAnimator.Animate(_scale, ScaleTransform.ScaleYProperty, 1, SpringSpec.Press, precision: 0.001);
    }
}
