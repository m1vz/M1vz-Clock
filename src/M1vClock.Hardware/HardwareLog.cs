using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware;

internal static partial class HardwareLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Não foi possível ler a seção {Section}.")]
    public static partial void SectionFailed(ILogger logger, Exception exception, string section);
}
