using System.Globalization;
using System.Text;

namespace M1vClock.App.Search;

public enum SearchItemKind
{
    Page,
    Action,
}

public sealed record SearchItem(string Id, string Title, string Subtitle, string IconKey, IReadOnlyList<string> Keywords, SearchItemKind Kind);

public static class SearchIndex
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<SearchItem> Query(IEnumerable<SearchItem> items, string query, int max = 12)
    {
        ArgumentNullException.ThrowIfNull(items);
        var tokens = Normalize(query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return items.Take(max).ToList();
        }

        var phrase = string.Join(' ', tokens);
        return items
            .Select(item => (Item: item, Score: Score(item, tokens, phrase)))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Item.Kind)
            .ThenBy(result => result.Item.Title, StringComparer.CurrentCulture)
            .Take(max)
            .Select(result => result.Item)
            .ToList();
    }

    private static int Score(SearchItem item, string[] tokens, string phrase)
    {
        var title = Normalize(item.Title);
        var titleWords = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keywords = item.Keywords.Select(Normalize).ToList();
        var subtitle = Normalize(item.Subtitle);

        var total = 0;
        foreach (var token in tokens)
        {
            var best = 0;
            if (title.StartsWith(token, StringComparison.Ordinal))
            {
                best = 100;
            }
            else if (titleWords.Any(w => w.StartsWith(token, StringComparison.Ordinal)))
            {
                best = 80;
            }
            else if (keywords.Any(k => k.StartsWith(token, StringComparison.Ordinal)))
            {
                best = 50;
            }
            else if (title.Contains(token, StringComparison.Ordinal))
            {
                best = 40;
            }
            else if (keywords.Any(k => k.Contains(token, StringComparison.Ordinal)))
            {
                best = 25;
            }
            else if (subtitle.Contains(token, StringComparison.Ordinal))
            {
                best = 10;
            }

            if (best == 0)
            {
                return 0;
            }

            total += best;
        }

        if (title.StartsWith(phrase, StringComparison.Ordinal))
        {
            total += 50;
        }

        return total;
    }
}
