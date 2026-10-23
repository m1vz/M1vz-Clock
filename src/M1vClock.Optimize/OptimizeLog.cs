using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize;

internal static partial class OptimizeLog
{
    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "Não foi possível ler os programas de inicialização de {Source}.")]
    public static partial void StartupSourceFailed(ILogger logger, Exception exception, string source);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Information, Message = "Não foi possível ler os dados do arquivo {Path}; segue sem descrição.")]
    public static partial void FileDetailsFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Warning, Message = "Não foi possível ler a lista de serviços pelo WMI.")]
    public static partial void ServicesFailed(ILogger logger, Exception exception);
}
