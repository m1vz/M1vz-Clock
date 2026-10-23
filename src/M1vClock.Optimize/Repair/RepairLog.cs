using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Repair;

internal static partial class RepairLog
{
    [LoggerMessage(EventId = 6050, Level = LogLevel.Warning, Message = "A verificação de componentes do Windows (DISM) falhou com o código {Code:X8}.")]
    public static partial void DismFailed(ILogger logger, int code);

    [LoggerMessage(EventId = 6051, Level = LogLevel.Warning, Message = "Não foi possível rodar a verificação de arquivos do sistema (SFC).")]
    public static partial void SfcStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6052, Level = LogLevel.Information, Message = "A verificação {Check} terminou como {Verdict}.")]
    public static partial void Finished(ILogger logger, RepairCheck check, RepairVerdict verdict);

    [LoggerMessage(EventId = 6054, Level = LogLevel.Warning, Message = "Não foi possível ler o log CBS do Windows.")]
    public static partial void CbsReadFailed(ILogger logger, Exception exception);
}
