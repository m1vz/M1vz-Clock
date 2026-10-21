using System.Globalization;

namespace M1vClock.Hardware.Wmi;

public sealed class WmiRow
{
    private const string DateFormat = "yyyyMMddHHmmss";
    private const int DatePrefixLength = 14;

    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

    public WmiRow(IEnumerable<KeyValuePair<string, object?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (name, value) in values)
        {
            _values[name] = value;
        }
    }

    public static WmiRow Of(params (string Name, object? Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new WmiRow(values.Select(v => KeyValuePair.Create(v.Name, v.Value)));
    }

    public object? Raw(string name) => _values.GetValueOrDefault(name);

    public string? Text(string name)
    {
        var text = Convert.ToString(Raw(name), CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public long? Whole(string name) => Raw(name) switch
    {
        null => null,
        long value => value,
        int value => value,
        uint value => value,
        ushort value => value,
        short value => value,
        byte value => value,
        sbyte value => value,
        ulong value when value <= long.MaxValue => (long)value,
        string value when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    public int Number(string name, int fallback = 0)
    {
        var value = Whole(name);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : fallback;
    }

    public ulong Size(string name) => Raw(name) switch
    {
        ulong value => value,
        string value when ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => Whole(name) is { } signed and >= 0 ? (ulong)signed : 0,
    };

    public double? Real(string name) => Raw(name) switch
    {
        null => null,
        double value => value,
        float value => value,
        decimal value => (double)value,
        string value when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => Whole(name),
    };

    public bool? Bool(string name) => Raw(name) switch
    {
        bool value => value,
        string value when bool.TryParse(value, out var parsed) => parsed,
        _ => null,
    };

    public DateTime? Date(string name)
    {
        if (Raw(name) is DateTime date)
        {
            return date;
        }

        var text = Text(name);
        if (text is null || text.Length < DatePrefixLength)
        {
            return null;
        }

        return DateTime.TryParseExact(text[..DatePrefixLength], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
