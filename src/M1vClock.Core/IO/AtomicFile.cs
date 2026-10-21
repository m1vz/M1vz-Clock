using System.Text;

namespace M1vClock.Core.IO;

public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string contents, string? backupPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? throw new ArgumentException("Caminho sem pasta.", nameof(path));
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = Utf8WithoutBom.GetBytes(contents);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(full))
            {
                File.Replace(temp, full, backupPath is null ? null : Path.GetFullPath(backupPath), ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, full);
            }
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }
}
