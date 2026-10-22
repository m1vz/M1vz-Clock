using Microsoft.Extensions.Logging;

namespace M1vClock.Diagnostics;

internal static partial class DiagnosticsLog
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Não foi possível ler o log de eventos {Log}.")]
    public static partial void EventLogFailed(ILogger logger, Exception exception, string log);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information, Message = "Sem permissão para ler o log de eventos {Log}; segue sem ele.")]
    public static partial void EventLogDenied(ILogger logger, string log);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Information, Message = "A leitura do log {Log} parou em {Limit} eventos; o resto fica de fora.")]
    public static partial void EventLogTruncated(ILogger logger, string log, int limit);
}
