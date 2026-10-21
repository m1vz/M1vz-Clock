using System.Windows;
using System.Windows.Controls;

namespace M1vClock.App.DesignSystem.Controls;

public sealed class StateBadge : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(MetricState), typeof(StateBadge), new PropertyMetadata(MetricState.Neutral));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StateBadge), new PropertyMetadata(string.Empty));

    public MetricState State
    {
        get => (MetricState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
