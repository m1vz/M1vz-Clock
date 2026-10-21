using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace M1vClock.Architecture.Tests;

public sealed partial class LocalizationKeyTests
{
    private const string PortuguesePath = "src/M1vClock.App/Localization/Strings.resx";
    private const string EnglishPath = "src/M1vClock.App/Localization/Strings.en.resx";
    private const int LongTextLength = 20;

    private static readonly string[] Prefixes =
    [
        "Action.", "Chart.", "Common.", "Crash.", "DS.", "Dialog.", "Error.", "History.", "Motion.", "Page.",
        "Hw.", "Placeholder.", "Search.", "Section.", "Settings.", "State.", "Token.", "Type.", "Window.",
    ];

    private static readonly string[] NotResourceKeys = ["Motion.Reduced"];

    private static readonly string[] SameInEveryLanguage = ["Page.Overclock.Keywords", "DS.Dialog.Change1"];

    [GeneratedRegex(@"\{l:Text\s+(?<key>[A-Za-z][A-Za-z0-9.]*)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$")]
    private static partial Regex KeyShape();

    [GeneratedRegex(@"\{(?<index>\d+)\}")]
    private static partial Regex Placeholder();

    [Theory]
    [InlineData(PortuguesePath)]
    [InlineData(EnglishPath)]
    public void ResourceFileHasNoDuplicateOrEmptyEntries(string path)
    {
        var entries = Load(path).ToList();

        var duplicates = entries.GroupBy(e => e.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var empty = entries.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key).ToList();

        Assert.True(duplicates.Count == 0, "chaves duplicadas: " + string.Join(", ", duplicates));
        Assert.True(empty.Count == 0, "textos vazios: " + string.Join(", ", empty));
    }

    [Fact]
    public void EveryKeyUsedInXamlExists()
    {
        var keys = Load(PortuguesePath).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var file in SourceTree.Files("src", "*.xaml"))
        {
            foreach (Match match in XamlKey().Matches(File.ReadAllText(file)))
            {
                var key = match.Groups["key"].Value;
                if (!keys.Contains(key))
                {
                    missing.Add($"{key} ({SourceTree.Relative(file)})");
                }
            }
        }

        Assert.True(missing.Count == 0, "chaves de texto que não existem: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryKeyUsedInCSharpExists()
    {
        var keys = Load(PortuguesePath).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var file in SourceTree.CSharpFiles("src"))
        {
            foreach (var token in SourceTree.ParseRoot(file).DescendantTokens().Where(t => t.IsKind(SyntaxKind.StringLiteralToken)))
            {
                var value = token.ValueText;
                if (LooksLikeKey(value) && !keys.Contains(value))
                {
                    missing.Add($"{value} ({SourceTree.Relative(file)})");
                }
            }
        }

        Assert.True(missing.Count == 0, "chaves de texto que não existem: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData(PortuguesePath)]
    [InlineData(EnglishPath)]
    public void EveryTextIsAValidFormatString(string path)
    {
        object[] arguments = ["a", "b", "c", "d", "e", "f"];
        var broken = new List<string>();
        foreach (var (key, value) in Load(path))
        {
            try
            {
                _ = string.Format(CultureInfo.InvariantCulture, value, arguments);
            }
            catch (FormatException ex)
            {
                broken.Add($"{key}: {ex.Message}");
            }
        }

        Assert.True(broken.Count == 0, "formato inválido: " + string.Join("; ", broken));
    }

    [Fact]
    public void EnglishHasExactlyTheKeysOfPortuguese()
    {
        var portuguese = Load(PortuguesePath).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var english = Load(EnglishPath).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

        var missing = portuguese.Except(english, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var extra = english.Except(portuguese, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, "faltam no inglês: " + string.Join(", ", missing));
        Assert.True(extra.Count == 0, "sobram no inglês: " + string.Join(", ", extra));
    }

    [Fact]
    public void EnglishKeepsTheSamePlaceholdersAsPortuguese()
    {
        var english = Load(EnglishPath).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        var different = new List<string>();
        foreach (var (key, value) in Load(PortuguesePath))
        {
            if (english.TryGetValue(key, out var translated) && !Indexes(value).SequenceEqual(Indexes(translated)))
            {
                different.Add(key);
            }
        }

        Assert.True(different.Count == 0, "marcadores {n} diferentes: " + string.Join(", ", different));
    }

    [Fact]
    public void EnglishIsNotALeftoverCopyOfPortuguese()
    {
        var english = Load(EnglishPath).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        var copied = Load(PortuguesePath)
            .Where(e => e.Value.Length >= LongTextLength && !SameInEveryLanguage.Contains(e.Key, StringComparer.Ordinal))
            .Where(e => english.TryGetValue(e.Key, out var translated) && translated == e.Value)
            .Select(e => e.Key)
            .ToList();

        Assert.True(copied.Count == 0, "texto em inglês igual ao português: " + string.Join(", ", copied));
    }

    private static List<string> Indexes(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups["index"].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    private static bool LooksLikeKey(string value) =>
        KeyShape().IsMatch(value)
        && Prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal))
        && !NotResourceKeys.Contains(value, StringComparer.Ordinal);

    private static IEnumerable<(string Key, string Value)> Load(string path)
    {
        var document = XDocument.Parse(SourceTree.Read(path));
        return document.Root!.Elements("data")
            .Select(data => (data.Attribute("name")!.Value, data.Element("value")?.Value ?? string.Empty));
    }
}
