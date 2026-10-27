using M1vClock.Tuning.Gpu;
using Microsoft.Extensions.Logging;

namespace M1vClock.Tuning;

internal static partial class TuningLog
{
    [LoggerMessage(EventId = 8001, Level = LogLevel.Warning, Message = "Não foi possível ler os erros de hardware do registro de eventos.")]
    public static partial void ErrorsUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Information, Message = "Biblioteca {Library} indisponível.")]
    public static partial void LibraryUnavailable(ILogger logger, Exception exception, string library);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Warning, Message = "Ajuste NVIDIA aplicado na GPU {Index}: energia {Power}, clock {Clock}.")]
    public static partial void NvidiaApplied(ILogger logger, int index, NvmlReturn power, NvmlReturn clock);

    [LoggerMessage(EventId = 8004, Level = LogLevel.Warning, Message = "GPU {Index} NVIDIA de volta ao padrão: energia {Power}, clock {Clock}.")]
    public static partial void NvidiaRestored(ILogger logger, int index, NvmlReturn power, NvmlReturn clock);

    [LoggerMessage(EventId = 8005, Level = LogLevel.Warning, Message = "Marcador de ajuste em {File} ilegível.")]
    public static partial void MarkerUnreadable(ILogger logger, Exception exception, string file);

    [LoggerMessage(EventId = 8006, Level = LogLevel.Warning, Message = "SessionGuard restaurou o padrão da GPU ({Reason}): {Status}.")]
    public static partial void GuardRestored(ILogger logger, GuardReason reason, NvidiaApplyStatus status);
}
