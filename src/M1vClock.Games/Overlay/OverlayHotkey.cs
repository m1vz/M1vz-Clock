using System.Globalization;

namespace M1vClock.Games.Overlay;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

public sealed record OverlayHotkey(HotkeyModifiers Modifiers, int VirtualKey, string KeyName)
{
    public const string Default = "Ctrl+Shift+F10";

    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Pause"] = 0x13,
        ["ScrollLock"] = 0x91,
    };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    public static OverlayHotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => (HotkeyModifiers?)null,
            };
            if (modifier is null || modifiers.HasFlag(modifier.Value))
            {
                return null;
            }

            modifiers |= modifier.Value;
        }

        if (Key(parts[^1]) is not var (code, name))
        {
            return null;
        }

        var isFunction = code is >= 0x70 and <= 0x87;
        return modifiers == HotkeyModifiers.None && !isFunction ? null : new OverlayHotkey(modifiers, code, name);
    }

    private static (int Code, string Name)? Key(string text)
    {
        if (text.Length == 1 && char.IsAsciiLetterOrDigit(text[0]))
        {
            var upper = char.ToUpperInvariant(text[0]);
            return (upper, upper.ToString());
        }

        if (text.Length is 2 or 3 && (text[0] is 'F' or 'f') && int.TryParse(text[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 24)
        {
            return (0x6F + number, "F" + number.ToString(CultureInfo.InvariantCulture));
        }

        return Named.TryGetValue(text, out var code) ? (code, Named.Keys.First(k => string.Equals(k, text, StringComparison.OrdinalIgnoreCase))) : null;
    }
}
