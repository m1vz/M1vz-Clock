using System.Windows;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.DesignSystem.Controls;

public sealed class Disclosure : FrameworkElement
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(Disclosure), new PropertyMetadata(true, OnIsExpandedChanged));

    public static readonly DependencyProperty ForegroundProperty = Icon.ForegroundProperty.AddOwner(typeof(Disclosure));

    private readonly Icon _icon;
    private readonly RotateTransform _rotation = new();

    public Disclosure()
    {
        _icon = new Icon { Size = 12, StrokeThickness = 2, RenderTransform = _rotation, RenderTransformOrigin = new Point(0.5, 0.5) };
        _icon.SetResourceReference(Icon.DataProperty, "Icon.ChevronDown");
        AddVisualChild(_icon);
        Loaded += (_, _) => SpringAnimator.Set(_rotation, RotateTransform.AngleProperty, IsExpanded ? 0 : -90);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _icon;

    protected override Size MeasureOverride(Size availableSize)
    {
        _icon.Measure(availableSize);
        return _icon.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _icon.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var disclosure = (Disclosure)d;
        var angle = (bool)e.NewValue ? 0 : -90;
        if (disclosure.IsLoaded)
        {
            SpringAnimator.Animate(disclosure._rotation, RotateTransform.AngleProperty, angle, SpringSpec.Standard, precision: 0.1);
        }
        else
        {
            SpringAnimator.Set(disclosure._rotation, RotateTransform.AngleProperty, angle);
        }
    }
}
