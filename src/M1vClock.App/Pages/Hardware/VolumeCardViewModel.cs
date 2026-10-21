using M1vClock.App.DesignSystem.Controls;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Hardware.Storage;

namespace M1vClock.App.Pages.Hardware;

public sealed class VolumeCardViewModel(Volume volume)
{
    public const double WarningFraction = 0.80;
    public const double CriticalFraction = 0.90;

    public string Title => string.IsNullOrWhiteSpace(volume.Label) ? volume.Letter : $"{volume.Letter}  {volume.Label}";

    public string Meta => string.Join(" · ", new[] { Loc.Get($"Hw.Storage.Kind.{volume.Kind}"), volume.FileSystem, volume.DiskModel }.Where(p => p.Length > 0));

    public string Sizes => Loc.Format("Hw.Storage.Volume.Sizes", Units.Bytes(volume.UsedBytes), Units.Bytes(volume.FreeBytes), Units.Bytes(volume.TotalBytes));

    public double Fraction => volume.UsedFraction;

    public string UsedText => Fraction.ToString("P0", Loc.Culture);

    public MetricState State => Fraction >= CriticalFraction ? MetricState.Critical : Fraction >= WarningFraction ? MetricState.Warning : MetricState.Neutral;

    public string AccessibleName => $"{Title}: {Loc.Format("Hw.Storage.Volume.Used", UsedText)}";
}
