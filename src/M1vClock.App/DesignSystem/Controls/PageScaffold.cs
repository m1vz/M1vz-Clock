using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using M1vClock.App.DesignSystem.Motion;

namespace M1vClock.App.DesignSystem.Controls;

[TemplatePart(Name = "PART_Scroll", Type = typeof(ScrollViewer))]
[TemplatePart(Name = "PART_Edge", Type = typeof(UIElement))]
public sealed class PageScaffold : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageScaffold), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(PageScaffold), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(PageScaffold));

    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register(
        "AnimatedOffset", typeof(double), typeof(PageScaffold), new PropertyMetadata(0.0, OnAnimatedOffsetChanged));

    private const double WheelStep = 48;
    private const double EdgeDistance = 24;

    private ScrollViewer? _scroll;
    private UIElement? _edge;
    private double _target;

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public override void OnApplyTemplate()
    {
        if (_scroll is not null)
        {
            _scroll.ScrollChanged -= OnScrollChanged;
            _scroll.PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        base.OnApplyTemplate();
        _scroll = GetTemplateChild("PART_Scroll") as ScrollViewer;
        _edge = GetTemplateChild("PART_Edge") as UIElement;
        if (_scroll is not null)
        {
            _scroll.ScrollChanged += OnScrollChanged;
            _scroll.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    public void ScrollTo(double offset)
    {
        if (_scroll is null)
        {
            return;
        }

        _target = Math.Clamp(offset, 0, _scroll.ScrollableHeight);
        SpringAnimator.Set(this, AnimatedOffsetProperty, _target);
    }

    private static void OnAnimatedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var scaffold = (PageScaffold)d;
        scaffold._scroll?.ScrollToVerticalOffset((double)e.NewValue);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_edge is not null)
        {
            _edge.Opacity = Math.Clamp(e.VerticalOffset / EdgeDistance, 0, 1);
        }

        if (!SpringAnimator.IsAnimating(this, AnimatedOffsetProperty))
        {
            _target = e.VerticalOffset;
            SetValue(AnimatedOffsetProperty, e.VerticalOffset);
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_scroll is null || e.Handled || HasNestedScroller(e.OriginalSource as DependencyObject))
        {
            return;
        }

        e.Handled = true;
        var start = SpringAnimator.IsAnimating(this, AnimatedOffsetProperty) ? _target : _scroll.VerticalOffset;
        _target = Math.Clamp(start - e.Delta / 120.0 * WheelStep, 0, _scroll.ScrollableHeight);
        SpringAnimator.Animate(this, AnimatedOffsetProperty, _target, SpringSpec.Quick, precision: 0.5);
    }

    private bool HasNestedScroller(DependencyObject? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, _scroll); node = ParentOf(node))
        {
            if (node is ScrollViewer nested && nested.ScrollableHeight > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
