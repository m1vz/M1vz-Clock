using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.DesignSystem.Controls;

public sealed class ToastCard : ContentControl
{
    public static readonly DependencyProperty IsExitingProperty = DependencyProperty.Register(
        nameof(IsExiting), typeof(bool), typeof(ToastCard), new PropertyMetadata(false, OnExitingChanged));

    private const double SlideDistance = 32;

    private readonly TranslateTransform _offset = new(SlideDistance, 0);

    public ToastCard()
    {
        RenderTransform = _offset;
        Opacity = 0;
        Loaded += (_, _) => Animate(0, 1, SpringSpec.Standard);
    }

    public bool IsExiting
    {
        get => (bool)GetValue(IsExitingProperty);
        set => SetValue(IsExitingProperty, value);
    }

    private static void OnExitingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            ((ToastCard)d).Animate(SlideDistance, 0, SpringSpec.Quick);
        }
    }

    private void Animate(double offset, double opacity, SpringSpec spec)
    {
        SpringAnimator.Animate(_offset, TranslateTransform.XProperty, offset, spec);
        SpringAnimator.Animate(this, OpacityProperty, opacity, spec);
    }
}
