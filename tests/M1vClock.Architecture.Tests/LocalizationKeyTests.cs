using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace M1vClock.Architecture.Tests;

public sealed partial class LocalizationKeyTests
{
    private const string ResourcePath = "src/M1vClock.App/Localization/Strings.resx";

    private static readonly string[] Prefixes =
    [
        "Action.", "Chart.", "Common.", "Crash.", "DS.", "Dialog.", "Error.", "History.", "Motion.", "Page.",
        "Placeholder.", "Search.", "Section.", "Settings.", "State.", "Token.", "Type.", "Window.",
    ];

    private static readonly string[] NotResourceKeys = ["Motion.Reduced"];

    [GeneratedRegex(@"\{l:Text\s+(?<key>[A-Za-z][A-Za-z0-9.]*)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$")]
    private static partial Regex KeyShape();

    [Fact]
    public void ResourceFileHasNoDuplicateOrEmptyEntries()
    {
        var entries = Load().ToList();

        var duplicates = entries.GroupBy(e => e.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var empty = entries.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key).ToList();

        Assert.True(duplicates.Count == 0, "chaves duplicadas: " + string.Join(", ", duplicates));
        Assert.True(empty.Count == 0, "textos vazios: " + string.Join(", ", empty));
    }

    [Fact]
    public void EveryKeyUsedInXamlExists()
    {
        var keys = Load().Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
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
        var keys = Load().Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
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

    [Fact]
    public void EveryTextIsAValidFormatString()
    {
        object[] arguments = ["a", "b", "c", "d", "e", "f"];
        var broken = new List<string>();
        foreach (var (key, value) in Load())
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

    private static bool LooksLikeKey(string value) =>
        KeyShape().IsMatch(value)
        && Prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal))
        && !NotResourceKeys.Contains(value, StringComparer.Ordinal);

    private static IEnumerable<(string Key, string Value)> Load()
    {
        var document = XDocument.Parse(SourceTree.Read(ResourcePath));
        return document.Root!.Elements("data")
            .Select(data => (data.Attribute("name")!.Value, data.Element("value")?.Value ?? string.Empty));
    }
}
