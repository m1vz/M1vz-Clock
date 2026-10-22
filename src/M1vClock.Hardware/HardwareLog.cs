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

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "{Source} não existe neste PC (normal em PC de mesa ou driver sem suporte).")]
    public static partial void SourceMissing(ILogger logger, string source);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning, Message = "{Failures} pacote(s) para {Address} não puderam ser enviados.")]
    public static partial void PingFailed(ILogger logger, Exception exception, string address, int failures);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Information, Message = "O DNS não resolveu {Host}.")]
    public static partial void DnsFailed(ILogger logger, Exception exception, string host);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning, Message = "O teste de {Direction} falhou.")]
    public static partial void SpeedTestFailed(ILogger logger, Exception exception, string direction);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Information, Message = "Nenhum adaptador de rede ativo com gateway; o diagnóstico não foi iniciado.")]
    public static partial void NoActiveAdapter(ILogger logger);

    [LoggerMessage(EventId = 3105, Level = LogLevel.Information, Message = "Dispositivos USB mudaram: {Added} conectado(s), {Removed} removido(s).")]
    public static partial void DeviceBatchDetected(ILogger logger, int added, int removed);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Error, Message = "Não foi possível abrir os sensores de hardware.")]
    public static partial void SensorOpenFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3202, Level = LogLevel.Warning, Message = "Falha ao ler os sensores; a leitura foi pulada.")]
    public static partial void SensorReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Information, Message = "Sensores de hardware abertos em {Milliseconds} ms.")]
    public static partial void SensorsOpened(ILogger logger, long milliseconds);

    [LoggerMessage(EventId = 3204, Level = LogLevel.Warning, Message = "A leitura dos sensores não parou a tempo ao encerrar.")]
    public static partial void SensorStopTimedOut(ILogger logger);
}
