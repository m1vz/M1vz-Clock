using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Tweaks;

internal static partial class TweakLog
{
    [LoggerMessage(EventId = 6040, Level = LogLevel.Warning, Message = "Não foi possível ler o estado do ajuste {Tweak}.")]
    public static partial void ReadFailed(ILogger logger, Exception exception, string tweak);

    [LoggerMessage(EventId = 6041, Level = LogLevel.Warning, Message = "O ajuste {Tweak} falhou no meio e as partes já aplicadas foram desfeitas.")]
    public static partial void RolledBack(ILogger logger, string tweak);

    [LoggerMessage(EventId = 6042, Level = LogLevel.Warning, Message = "O ajuste do mouse foi gravado, mas o Windows não aplicou na hora.")]
    public static partial void MouseLiveFailed(ILogger logger);
}
