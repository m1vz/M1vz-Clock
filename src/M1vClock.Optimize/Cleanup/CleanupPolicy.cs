namespace M1vClock.Optimize.Cleanup;

public static class CleanupPolicy
{
    public const int MinimumDepth = 3;

    private static readonly string[] ForbiddenNames =
    [
        "Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music", "OneDrive", "Documentos", "Imagens", "Vídeos", "Músicas",
        "Program Files", "Program Files (x86)", "System32", "SysWOW64", "WinSxS", "Users", "Windows",
    ];

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path).TrimEnd('\\', '/');
    }

    public static bool IsAcceptableRoot(string root, out string reason)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(root))
        {
            reason = "A raiz está vazia.";
            return false;
        }

        var full = Normalize(root);
        var parts = full.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < MinimumDepth)
        {
            reason = "A raiz é larga demais.";
            return false;
        }

        if (ForbiddenNames.Contains(parts[^1], StringComparer.OrdinalIgnoreCase))
        {
            reason = "A raiz é uma pasta pessoal ou do sistema.";
            return false;
        }

        if (parts.Length >= 2 && string.Equals(parts[^2], "Users", StringComparison.OrdinalIgnoreCase))
        {
            reason = "A raiz é a pasta de um usuário.";
            return false;
        }

        if (new DirectoryInfo(full) is { Exists: true } info && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            reason = "A raiz é um link ou junção.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static bool IsInside(string root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        var full = Normalize(path);
        var prefix = Normalize(root) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
