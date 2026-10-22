using System.Globalization;
using M1vClock.App.Localization;

namespace M1vClock.App.Formatting;

public static class Units
{
    public const string Missing = "—";

    private const double Step = 1024;
    private const double DecimalStep = 1000;

    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Bytes(double bytes, CultureInfo? culture = null)
    {
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes < 0)
        {
            return Missing;
        }

        var unit = 0;
        var value = bytes;
        while (value >= Step && unit < ByteUnits.Length - 1)
        {
            value /= Step;
            unit++;
        }

        return string.Create(culture ?? Loc.Culture, $"{Math.Round(value, 2):0.##} {ByteUnits[unit]}");
    }

    public static string DecimalBytes(double bytes, CultureInfo? culture = null)
    {
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes <= 0)
        {
            return Missing;
        }

        var unit = 0;
        var value = bytes;
        while (value >= DecimalStep && unit < ByteUnits.Length - 1)
        {
            value /= DecimalStep;
            unit++;
        }

        return string.Create(culture ?? Loc.Culture, $"{Math.Round(value, 1):0.#} {ByteUnits[unit]}");
    }

    public static string Kilobytes(double kilobytes, CultureInfo? culture = null) => Bytes(kilobytes * Step, culture);

    public static string Frequency(double megahertz, CultureInfo? culture = null)
    {
        if (double.IsNaN(megahertz) || megahertz <= 0)
        {
            return Missing;
        }

        return megahertz >= 1000
            ? string.Create(culture ?? Loc.Culture, $"{megahertz / 1000:0.00} GHz")
            : string.Create(culture ?? Loc.Culture, $"{megahertz:0} MHz");
    }

    public static string YesNo(bool? value) => value switch
    {
        true => Loc.Get("Hw.Common.Yes"),
        false => Loc.Get("Hw.Common.No"),
        null => Missing,
    };

    public static string Date(DateOnly? date, CultureInfo? culture = null) =>
        date is null ? Missing : date.Value.ToString("d", culture ?? Loc.Culture);

    public static string OrMissing(string? text) => string.IsNullOrWhiteSpace(text) ? Missing : text.Trim();
}
