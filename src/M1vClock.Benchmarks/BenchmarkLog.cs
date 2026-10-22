using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks;

internal static partial class BenchmarkLog
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Pasta temporária de teste criada em {Path}.")]
    public static partial void TempAreaCreated(ILogger logger, string path);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "Não foi possível apagar {Path}; a limpeza de sobras tenta de novo na próxima abertura.")]
    public static partial void TempDeleteFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 4006, Level = LogLevel.Debug, Message = "{Path} estava em uso por outro programa; tentativa {Attempt} de apagar.")]
    public static partial void TempDeleteRetry(ILogger logger, Exception exception, string path, int attempt);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Information, Message = "Sobras de teste anteriores removidas: {Count} pasta(s) em {Parent}.")]
    public static partial void LeftoversRemoved(ILogger logger, int count, string parent);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Warning, Message = "Sobra de teste ignorada porque não é uma pasta comum: {Path}.")]
    public static partial void LeftoverSkipped(ILogger logger, string path);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Warning, Message = "Não foi possível verificar sobras de teste em {Parent}.")]
    public static partial void SweepFailed(ILogger logger, Exception exception, string parent);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning, Message = "Importação de resultados recusada: {Reason}.")]
    public static partial void ImportFailed(ILogger logger, Exception? exception, Results.ImportFailure reason);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Error, Message = "Não foi possível gravar o histórico de benchmarks.")]
    public static partial void HistorySaveFailed(ILogger logger, Exception exception);
}
