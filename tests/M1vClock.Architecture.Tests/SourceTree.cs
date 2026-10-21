using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace M1vClock.Architecture.Tests;

internal static class SourceTree
{
    private static readonly string[] IgnoredFolders = ["bin", "obj", "_ref", ".git"];

    public static string Root { get; } = FindRoot();

    public static IReadOnlyList<string> Files(string folder, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, folder), pattern, SearchOption.AllDirectories)
            .Where(path => !IsIgnored(path))
            .Order(StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<string> CSharpFiles(params string[] folders) =>
        folders.SelectMany(folder => Files(folder, "*.cs")).ToList();

    public static SyntaxTree Parse(string path) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path);

    public static SyntaxNode ParseRoot(string path) => Parse(path).GetRoot();

    public static string Relative(string path) =>
        Path.GetRelativePath(Root, path).Replace('\\', '/');

    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath));

    private static bool IsIgnored(string path) =>
        Path.GetRelativePath(Root, path)
            .Split(Path.DirectorySeparatorChar)
            .Any(part => IgnoredFolders.Contains(part, StringComparer.Ordinal));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "m1vClock.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"m1vClock.slnx não foi encontrado acima de {AppContext.BaseDirectory}.");
    }
}
