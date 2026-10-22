using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace M1vClock.App.DesignSystem.Controls;

public sealed class StateBadge : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(MetricState), typeof(StateBadge), new PropertyMetadata(MetricState.Neutral));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StateBadge), new PropertyMetadata(string.Empty, OnTextChanged));

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

    protected override AutomationPeer OnCreateAutomationPeer() => new StateBadgeAutomationPeer(this);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (UIElementAutomationPeer.FromElement((StateBadge)d) is not { } peer)
        {
            return;
        }

        peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, e.OldValue as string ?? string.Empty, e.NewValue as string ?? string.Empty);
        peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private sealed class StateBadgeAutomationPeer(StateBadge owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(StateBadge);

        protected override string GetNameCore() => ((StateBadge)Owner).Text;

        protected override AutomationLiveSetting GetLiveSettingCore() => AutomationLiveSetting.Polite;

        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
