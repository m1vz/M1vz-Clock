using M1vClock.Diagnostics.Crashes;
using Microsoft.Extensions.Logging;

namespace M1vClock.Diagnostics;

internal static partial class DiagnosticsLog
{
    [LoggerMessage(EventId = 5001, Level = LogLevel.Warning, Message = "Não foi possível ler o log de eventos {Log}.")]
    public static partial void EventLogFailed(ILogger logger, Exception exception, string log);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Sem permissão para ler o log de eventos {Log}; segue sem ele.")]
    public static partial void EventLogDenied(ILogger logger, string log);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information, Message = "A leitura do log {Log} parou em {Limit} eventos; o resto fica de fora.")]
    public static partial void EventLogTruncated(ILogger logger, string log, int limit);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Warning, Message = "Não foi possível ler a configuração de dumps do Windows.")]
    public static partial void CrashSettingsFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Information, Message = "Sem permissão para ler a pasta de dumps {Folder}; segue sem ela.")]
    public static partial void DumpFolderDenied(ILogger logger, DumpFolder folder);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Warning, Message = "Não foi possível ler a pasta de dumps {Folder}.")]
    public static partial void DumpFolderFailed(ILogger logger, Exception exception, DumpFolder folder);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Information, Message = "Um evento veio com XML que não dá para ler; foi ignorado.")]
    public static partial void EventXmlInvalid(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5008, Level = LogLevel.Information, Message = "Não foi possível ler o cabeçalho do dump {Name}; segue sem o código.")]
    public static partial void DumpHeaderUnreadable(ILogger logger, Exception exception, string name);

    [LoggerMessage(EventId = 5009, Level = LogLevel.Warning, Message = "Não foi possível ler a confiabilidade ({Part}) pelo WMI.")]
    public static partial void ReliabilityFailed(ILogger logger, Exception exception, string part);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Warning, Message = "Não foi possível listar a pasta de logs.")]
    public static partial void LogFolderFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5011, Level = LogLevel.Warning, Message = "Não foi possível ler o log {Name}.")]
    public static partial void LogFileFailed(ILogger logger, Exception exception, string name);
}
