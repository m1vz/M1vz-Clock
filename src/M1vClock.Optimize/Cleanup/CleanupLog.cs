using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Cleanup;

internal static partial class CleanupLog
{
    [LoggerMessage(EventId = 6010, Level = LogLevel.Debug, Message = "Não foi possível ler o tamanho de {Path}.")]
    public static partial void FileUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 6011, Level = LogLevel.Debug, Message = "{Path} ficou, porque está em uso ou protegido.")]
    public static partial void FileKept(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 6012, Level = LogLevel.Warning, Message = "A pasta {Root} foi recusada pela limpeza: {Reason}")]
    public static partial void RootRefused(ILogger logger, string root, string reason);

    [LoggerMessage(EventId = 6013, Level = LogLevel.Information, Message = "Limpeza {Category}: {Deleted} arquivo(s) apagados, {Freed} bytes liberados, {Failed} ficaram.")]
    public static partial void Cleaned(ILogger logger, string category, int deleted, long freed, int failed);
}
