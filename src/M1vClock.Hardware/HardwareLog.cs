using Microsoft.Extensions.Logging;

namespace M1vClock.Hardware;

internal static partial class HardwareLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Não foi possível ler a seção {Section}.")]
    public static partial void SectionFailed(ILogger logger, Exception exception, string section);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Sem permissão para ler {Source}; segue com o que o Windows libera sem administrador.")]
    public static partial void OptionalSourceDenied(ILogger logger, string source);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Não foi possível ler {Source}; segue sem esse dado.")]
    public static partial void OptionalSourceFailed(ILogger logger, Exception exception, string source);
}
