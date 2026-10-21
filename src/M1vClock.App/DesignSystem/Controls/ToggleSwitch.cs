using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.DesignSystem.Controls;

[TemplatePart(Name = KnobPart, Type = typeof(FrameworkElement))]
[TemplatePart(Name = TrackOnPart, Type = typeof(UIElement))]
public sealed class ToggleSwitch : ToggleButton
{
    private const string KnobPart = "PART_Knob";
    private const string TrackOnPart = "PART_TrackOn";
    private const double TrackWidth = 40;
    private const double KnobSize = 20;
    private const double PressedKnobWidth = 24;
    private const double Inset = 2;
    private const double DragThreshold = 3;
    private const double FlickVelocity = 60;

    private readonly VelocityTracker _velocity = new();
    private readonly TranslateTransform _knobOffset = new();
    private FrameworkElement? _knob;
    private UIElement? _trackOn;
    private bool _pointerDown;
    private bool _dragging;
    private double _grabOffset;
    private double _downX;
    private double? _releaseVelocity;

    private static double OffX => Inset;

    private static double OnX(double knobWidth) => TrackWidth - Inset - knobWidth;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _knob = GetTemplateChild(KnobPart) as FrameworkElement;
        _trackOn = GetTemplateChild(TrackOnPart) as UIElement;
        if (_knob is not null)
        {
            _knob.RenderTransform = _knobOffset;
            _knob.Width = KnobSize;
        }

        SpringAnimator.Set(_knobOffset, TranslateTransform.XProperty, IsChecked == true ? OnX(KnobSize) : OffX);
        if (_trackOn is not null)
        {
            SpringAnimator.Set(_trackOn, OpacityProperty, IsChecked == true ? 1 : 0);
        }
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        AnimateToState();
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        AnimateToState();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!IsEnabled || _knob is null)
        {
            return;
        }

        e.Handled = true;
        Focus();
        CaptureMouse();
        _pointerDown = true;
        _dragging = false;
        _downX = e.GetPosition(this).X;
        _grabOffset = _downX - _knobOffset.X;
        _velocity.Reset();
        _velocity.Add(Now(), _knobOffset.X);

        SpringAnimator.Animate(_knob, WidthProperty, PressedKnobWidth, SpringSpec.Press, precision: 0.05);
        if (IsChecked == true)
        {
            SpringAnimator.Animate(_knobOffset, TranslateTransform.XProperty, OnX(PressedKnobWidth), SpringSpec.Press, precision: 0.05);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!_pointerDown || _knob is null)
        {
            return;
        }

        var x = e.GetPosition(this).X;
        if (!_dragging && Math.Abs(x - _downX) > DragThreshold)
        {
            _dragging = true;
        }

        if (!_dragging)
        {
            return;
        }

        var raw = x - _grabOffset;
        var max = OnX(_knob.ActualWidth > 0 ? _knob.ActualWidth : PressedKnobWidth);
        var position = raw < OffX
            ? OffX - SpringMath.RubberBand(OffX - raw, TrackWidth)
            : raw > max
                ? max + SpringMath.RubberBand(raw - max, TrackWidth)
                : raw;
        SpringAnimator.Set(_knobOffset, TranslateTransform.XProperty, position);
        _velocity.Add(Now(), position);
        if (_trackOn is not null)
        {
            SpringAnimator.Set(_trackOn, OpacityProperty, Math.Clamp((position - OffX) / (max - OffX), 0, 1));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!_pointerDown)
        {
            return;
        }

        e.Handled = true;
        var wasDragging = _dragging;
        var inside = IsMouseOver;
        EndPointer();

        if (!wasDragging)
        {
            if (inside)
            {
                OnClick();
            }
            else
            {
                AnimateToState();
            }

            return;
        }

        var velocity = _velocity.Velocity();
        var projected = _knobOffset.X + SpringMath.Project(velocity);
        var middle = (OffX + OnX(KnobSize)) / 2;
        var turnOn = Math.Abs(velocity) > FlickVelocity ? velocity > 0 : projected > middle;
        _releaseVelocity = velocity;
        if (turnOn != (IsChecked == true))
        {
            IsChecked = turnOn;
        }
        else
        {
            AnimateToState();
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_pointerDown)
        {
            EndPointer();
            AnimateToState();
        }
    }

    private void EndPointer()
    {
        _pointerDown = false;
        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (_knob is not null)
        {
            SpringAnimator.Animate(_knob, WidthProperty, KnobSize, SpringSpec.Quick, precision: 0.05);
        }
    }

    private void AnimateToState()
    {
        if (_knob is null)
        {
            return;
        }

        var on = IsChecked == true;
        var velocity = _releaseVelocity;
        _releaseVelocity = null;
        var spec = velocity is double v && Math.Abs(v) > FlickVelocity ? SpringSpec.Momentum : SpringSpec.Standard;
        SpringAnimator.Animate(_knobOffset, TranslateTransform.XProperty, on ? OnX(KnobSize) : OffX, spec, velocity, precision: 0.05);
        if (_trackOn is not null)
        {
            SpringAnimator.Animate(_trackOn, OpacityProperty, on ? 1 : 0, SpringSpec.Quick, precision: 0.005);
        }
    }

    private static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}
