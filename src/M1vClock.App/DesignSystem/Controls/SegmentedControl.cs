using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.DesignSystem.Controls;

[TemplatePart(Name = IndicatorPart, Type = typeof(FrameworkElement))]
public sealed class SegmentedControl : ListBox
{
    private const string IndicatorPart = "PART_Indicator";
    private const double Gap = 2;
    private readonly TranslateTransform _indicatorOffset = new();
    private FrameworkElement? _indicator;
    private bool _placed;

    public SegmentedControl()
    {
        SelectionMode = SelectionMode.Single;
        SizeChanged += (_, _) => PlaceIndicator(animated: false);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _indicator = GetTemplateChild(IndicatorPart) as FrameworkElement;
        if (_indicator is not null)
        {
            _indicator.RenderTransform = _indicatorOffset;
        }

        _placed = false;
        Dispatcher.BeginInvoke(() => PlaceIndicator(animated: false), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    protected override DependencyObject GetContainerForItemOverride() => new SegmentedItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is SegmentedItem;

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        PlaceIndicator(animated: _placed);
    }

    private void PlaceIndicator(bool animated)
    {
        if (_indicator is null || Items.Count == 0 || ActualWidth <= 0)
        {
            return;
        }

        var inner = ActualWidth - Padding.Left - Padding.Right - BorderThickness.Left - BorderThickness.Right;
        var cell = inner / Items.Count;
        _indicator.Width = Math.Max(0, cell - Gap);
        _indicator.Visibility = SelectedIndex >= 0 ? Visibility.Visible : Visibility.Hidden;
        var x = Math.Max(0, SelectedIndex) * cell + Gap / 2;
        if (animated)
        {
            SpringAnimator.Animate(_indicatorOffset, TranslateTransform.XProperty, x, SpringSpec.Standard, precision: 0.05);
        }
        else
        {
            SpringAnimator.Set(_indicatorOffset, TranslateTransform.XProperty, x);
        }

        _placed = true;
    }
}

public sealed class SegmentedItem : ListBoxItem
{
}
