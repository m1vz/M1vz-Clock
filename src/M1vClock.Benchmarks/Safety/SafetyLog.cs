using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks.Safety;

internal static partial class SafetyLog
{
    [LoggerMessage(EventId = 4201, Level = LogLevel.Warning, Message = "Teste interrompido pelo monitor de segurança: {Reason} ({Component}) valor {Value}, limite {Limit}.")]
    public static partial void Tripped(ILogger logger, TripReason reason, string component, double value, double limit);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Warning, Message = "Não foi possível ler a temperatura de {Component}.")]
    public static partial void ProbeFailed(ILogger logger, Exception exception, ThermalComponent component);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Information, Message = "Teste de estresse iniciado: CPU {Cpu}, RAM {Ram}, GPU {Gpu}, duração {Minutes} min.")]
    public static partial void StressStarted(ILogger logger, bool cpu, bool ram, bool gpu, double minutes);

    [LoggerMessage(EventId = 4204, Level = LogLevel.Information, Message = "Teste de estresse encerrado: {Reason} após {Seconds} s.")]
    public static partial void StressEnded(ILogger logger, Stress.StressEnd reason, int seconds);

    [LoggerMessage(EventId = 4205, Level = LogLevel.Warning, Message = "Teste de estresse recusado: {Reason}.")]
    public static partial void StressRefused(ILogger logger, Stress.StressStartStatus reason);

    [LoggerMessage(EventId = 4206, Level = LogLevel.Warning, Message = "Só foi possível reservar {Allocated} MB dos {Wanted} MB pedidos para o teste de memória.")]
    public static partial void MemoryShort(ILogger logger, long allocated, long wanted);

    [LoggerMessage(EventId = 4207, Level = LogLevel.Warning, Message = "A reserva de memória do teste falhou ao pedir {Wanted} MB.")]
    public static partial void MemoryAllocationFailed(ILogger logger, Exception exception, long wanted);
}
