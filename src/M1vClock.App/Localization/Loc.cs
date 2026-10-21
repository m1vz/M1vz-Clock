using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace M1vClock.App.Localization;

public static class Loc
{
    private static readonly ResourceManager Resources = new("M1vClock.App.Localization.Strings", typeof(Loc).Assembly);

    public static CultureInfo Culture { get; set; } = CultureInfo.GetCultureInfo("pt-BR");

    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Resources.GetString(key, Culture) ?? $"[{key}]";
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(Culture, Get(key), arguments);

    public static bool Exists(string key) => Resources.GetString(key, CultureInfo.InvariantCulture) is not null;
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public TextExtension()
    {
    }

    public TextExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
