using Microsoft.Extensions.Logging;

namespace M1vClock.Benchmarks.Gpu;

internal static partial class GpuLog
{
    [LoggerMessage(EventId = 4301, Level = LogLevel.Error, Message = "O teste de estresse de GPU parou com erro.")]
    public static partial void StressFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Warning, Message = "Não foi possível verificar o ray tracing por hardware; o Direct3D 12 não respondeu.")]
    public static partial void RayTracingCheckFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4303, Level = LogLevel.Information, Message = "Nenhuma GPU de hardware encontrada para verificar o ray tracing.")]
    public static partial void NoGpuForRayTracing(ILogger logger, Exception exception);
}
