using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks;

internal static partial class BenchmarkLog
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Pasta temporária de teste criada em {Path}.")]
    public static partial void TempAreaCreated(ILogger logger, string path);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "Não foi possível apagar {Path}; a limpeza de sobras tenta de novo na próxima abertura.")]
    public static partial void TempDeleteFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Information, Message = "Sobras de teste anteriores removidas: {Count} pasta(s) em {Parent}.")]
    public static partial void LeftoversRemoved(ILogger logger, int count, string parent);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Warning, Message = "Sobra de teste ignorada porque não é uma pasta comum: {Path}.")]
    public static partial void LeftoverSkipped(ILogger logger, string path);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Warning, Message = "Não foi possível verificar sobras de teste em {Parent}.")]
    public static partial void SweepFailed(ILogger logger, Exception exception, string parent);
}
