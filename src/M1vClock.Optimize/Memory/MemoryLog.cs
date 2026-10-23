using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Memory;

internal static partial class MemoryLog
{
    [LoggerMessage(EventId = 6020, Level = LogLevel.Warning, Message = "Não foi possível ler as listas de memória do Windows (código {Status:X8}).")]
    public static partial void ReadFailed(ILogger logger, int status);

    [LoggerMessage(EventId = 6021, Level = LogLevel.Information, Message = "A memória em espera foi liberada.")]
    public static partial void Purged(ILogger logger);

    [LoggerMessage(EventId = 6022, Level = LogLevel.Warning, Message = "O Windows recusou liberar a memória em espera (código {Status:X8}).")]
    public static partial void PurgeFailed(ILogger logger, int status);
}
