using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Diagnostics;

public static class DiagnosticsModule
{
    public static IServiceCollection AddDiagnosticsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
