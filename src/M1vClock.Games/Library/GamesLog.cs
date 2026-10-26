using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Library;

internal static partial class GamesLog
{
    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning, Message = "Não foi possível ler a biblioteca {Platform}.")]
    public static partial void LibraryFailed(ILogger logger, Exception exception, GamePlatform platform);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning, Message = "Arquivo de jogo ignorado por estar ilegível: {File}.")]
    public static partial void FileSkipped(ILogger logger, Exception exception, string file);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Information, Message = "Biblioteca de jogos lida: {Count} jogos.")]
    public static partial void Read(ILogger logger, int count);

    [LoggerMessage(EventId = 7014, Level = LogLevel.Warning, Message = "Não foi possível gravar as configurações de jogos.")]
    public static partial void SettingsSaveFailed(ILogger logger, Exception exception);
}
