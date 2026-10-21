using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace M1vClock.Architecture.Tests;

public sealed class CodeStyleTests
{
    private static readonly SyntaxKind[] CommentKinds =
    [
        SyntaxKind.SingleLineCommentTrivia,
        SyntaxKind.MultiLineCommentTrivia,
        SyntaxKind.SingleLineDocumentationCommentTrivia,
        SyntaxKind.MultiLineDocumentationCommentTrivia,
    ];

    private static readonly string[] LoggerPrefixes = ["AppLog.", "CoreLog.", "Log.", "_logger.", "logger."];

    [Fact]
    public void CSharpCodeHasNoComments()
    {
        var found = new List<string>();
        foreach (var file in SourceTree.CSharpFiles("src", "tests"))
        {
            foreach (var trivia in SourceTree.ParseRoot(file).DescendantTrivia())
            {
                if (CommentKinds.Contains(trivia.Kind()))
                {
                    found.Add($"{SourceTree.Relative(file)}:{trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
                }
            }
        }

        Assert.True(found.Count == 0, "Comentários encontrados:" + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    [Fact]
    public void XamlHasNoComments()
    {
        var found = SourceTree.Files("src", "*.xaml")
            .Where(file => File.ReadAllText(file).Contains("<!--", StringComparison.Ordinal))
            .Select(SourceTree.Relative)
            .ToList();

        Assert.True(found.Count == 0, "XAML com comentário: " + string.Join(", ", found));
    }

    [Fact]
    public void NoCatchBlockIsEmpty()
    {
        var empty = Catches()
            .Where(c => c.Clause.Block.Statements.Count == 0)
            .Select(c => c.Location)
            .ToList();

        Assert.True(empty.Count == 0, "catch vazio em: " + string.Join(", ", empty));
    }

    [Fact]
    public void EveryCatchLogsRethrowsOrPassesTheExceptionOn()
    {
        var silent = new List<string>();
        foreach (var (clause, location) in Catches())
        {
            var type = clause.Declaration?.Type.ToString() ?? string.Empty;
            if (type.EndsWith("OperationCanceledException", StringComparison.Ordinal) || IsAllowedProbe(location, type))
            {
                continue;
            }

            if (Rethrows(clause) || CallsLogger(clause) || UsesException(clause))
            {
                continue;
            }

            silent.Add($"{location} ({(type.Length == 0 ? "catch" : type)})");
        }

        Assert.True(silent.Count == 0, "catch que engole o erro sem registrar: " + string.Join(", ", silent));
    }

    [Fact]
    public void CatchWithoutATypeAlwaysRethrows()
    {
        var catchAll = Catches()
            .Where(c => c.Clause.Declaration is null && !Rethrows(c.Clause))
            .Select(c => c.Location)
            .ToList();

        Assert.True(catchAll.Count == 0, "catch sem tipo que não relança: " + string.Join(", ", catchAll));
    }

    private static bool IsAllowedProbe(string location, string type) =>
        location.StartsWith("src/M1vClock.Core/Processes/ProcessRunner.cs", StringComparison.Ordinal) && type == "ArgumentException";

    private static bool Rethrows(CatchClauseSyntax clause) =>
        clause.Block.DescendantNodes().Any(node => node is ThrowStatementSyntax or ThrowExpressionSyntax);

    private static bool CallsLogger(CatchClauseSyntax clause) =>
        clause.Block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(call => LoggerPrefixes.Any(prefix => call.Expression.ToString().StartsWith(prefix, StringComparison.Ordinal)));

    private static bool UsesException(CatchClauseSyntax clause)
    {
        var name = clause.Declaration?.Identifier.ValueText;
        return !string.IsNullOrEmpty(name)
            && clause.Block.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id => id.Identifier.ValueText == name);
    }

    private static IEnumerable<(CatchClauseSyntax Clause, string Location)> Catches()
    {
        foreach (var file in SourceTree.CSharpFiles("src", "tests"))
        {
            var tree = SourceTree.Parse(file);
            foreach (var clause in SourceTree.ParseRoot(file).DescendantNodes().OfType<CatchClauseSyntax>())
            {
                var line = tree.GetLineSpan(clause.Span).StartLinePosition.Line + 1;
                yield return (clause, $"{SourceTree.Relative(file)}:{line}");
            }
        }
    }
}
