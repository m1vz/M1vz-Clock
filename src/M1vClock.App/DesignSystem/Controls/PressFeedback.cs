using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.DesignSystem.Controls;

public static class PressFeedback
{
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached(
        "Scale", typeof(double), typeof(PressFeedback), new PropertyMetadata(1.0, OnScaleChanged));

    private static readonly DependencyProperty IsHookedProperty = DependencyProperty.RegisterAttached(
        "IsHooked", typeof(bool), typeof(PressFeedback), new PropertyMetadata(false));

    public static double GetScale(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(ScaleProperty);
    }

    public static void SetScale(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ScaleProperty, value);
    }

    private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || (bool)element.GetValue(IsHookedProperty) || (double)e.NewValue >= 1)
        {
            return;
        }

        element.SetValue(IsHookedProperty, true);
        element.PreviewMouseLeftButtonDown += (_, _) => Press(element);
        element.PreviewMouseLeftButtonUp += (_, _) => Release(element);
        element.MouseLeave += (_, _) => Release(element);
        element.LostMouseCapture += (_, _) => Release(element);
        element.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is Key.Space && !args.IsRepeat)
            {
                Press(element);
            }
        };
        element.PreviewKeyUp += (_, args) =>
        {
            if (args.Key is Key.Space)
            {
                Release(element);
            }
        };
    }

    private static void Press(UIElement element)
    {
        if (!element.IsEnabled)
        {
            return;
        }

        var transform = EnsureTransform(element);
        var scale = GetScale(element);
        SpringAnimator.Animate(transform, ScaleTransform.ScaleXProperty, scale, SpringSpec.Press, precision: 0.0005);
        SpringAnimator.Animate(transform, ScaleTransform.ScaleYProperty, scale, SpringSpec.Press, precision: 0.0005);
    }

    private static void Release(UIElement element)
    {
        if (element.RenderTransform is not ScaleTransform transform)
        {
            return;
        }

        SpringAnimator.Animate(transform, ScaleTransform.ScaleXProperty, 1, SpringSpec.Quick, precision: 0.0005);
        SpringAnimator.Animate(transform, ScaleTransform.ScaleYProperty, 1, SpringSpec.Quick, precision: 0.0005);
    }

    private static ScaleTransform EnsureTransform(UIElement element)
    {
        if (element.RenderTransform is ScaleTransform existing && !existing.IsFrozen)
        {
            return existing;
        }

        var transform = new ScaleTransform(1, 1);
        element.RenderTransform = transform;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return transform;
    }
}
