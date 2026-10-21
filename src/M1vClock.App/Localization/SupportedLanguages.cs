using System.Globalization;

namespace M1vClock.App.Localization;

public static class SupportedLanguages
{
    public const string Portuguese = "pt-BR";

    public const string English = "en";

    public static IReadOnlyList<string> All { get; } = [Portuguese, English];

    public static CultureInfo Resolve(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return CultureInfo.GetCultureInfo(Portuguese);
        }

        var primary = tag.Split('-')[0];
        return primary.Equals(English, StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo(English)
            : CultureInfo.GetCultureInfo(Portuguese);
    }

    public static int IndexOf(string? tag) => Resolve(tag).Name == English ? 1 : 0;

    public static string TagAt(int index) => index == 1 ? English : Portuguese;
}
