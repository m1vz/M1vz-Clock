using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Frames;

internal static partial class FramesLog
{
    [LoggerMessage(EventId = 7010, Level = LogLevel.Information, Message = "Medição de quadros iniciada: {State}.")]
    public static partial void MonitorStarted(ILogger logger, FrameMonitorState state);

    [LoggerMessage(EventId = 7011, Level = LogLevel.Warning, Message = "O Windows negou a sessão de medição de quadros.")]
    public static partial void EtwDenied(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7012, Level = LogLevel.Warning, Message = "A sessão de medição de quadros falhou.")]
    public static partial void EtwFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7013, Level = LogLevel.Debug, Message = "Não foi possível ler o nome do processo {ProcessId}.")]
    public static partial void ProcessNameUnknown(ILogger logger, Exception exception, int processId);
}
