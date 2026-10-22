using M1vClock.Diagnostics.Events;
using Microsoft.Extensions.DependencyInjection;

namespace M1vClock.Diagnostics;

public static class DiagnosticsModule
{
    public static IServiceCollection AddDiagnosticsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IEventLogSource, WindowsEventLogSource>();
        services.AddSingleton<IEventService, EventService>();
        return services;
    }
}
