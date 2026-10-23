using M1vClock.App.DesignSystem.Controls;

namespace M1vClock.App.Pages.Diagnostics;

public sealed class DiagnosticRowViewModel(string title, string detail, string meta, string badge, MetricState tone, string trailingTop, string trailingBottom)
{
    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string Meta { get; } = meta;

    public string Badge { get; } = badge;

    public MetricState Tone { get; } = tone;

    public string TrailingTop { get; } = trailingTop;

    public string TrailingBottom { get; } = trailingBottom;

    public bool HasDetail => Detail.Length > 0;

    public bool HasMeta => Meta.Length > 0;

    public bool HasBadge => Badge.Length > 0;

    public bool HasTrailing => TrailingTop.Length > 0 || TrailingBottom.Length > 0;

    public bool HasBadgeOnly => HasBadge && !HasTrailing;

    public string AccessibleName => string.Join(". ", new[] { Badge, Title, TrailingTop, TrailingBottom }.Where(s => s.Length > 0));

    public override string ToString() => AccessibleName;
}
