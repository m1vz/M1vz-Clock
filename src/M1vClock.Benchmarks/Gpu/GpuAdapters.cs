using Vortice.DXGI;

namespace M1vClock.Benchmarks.Gpu;

public sealed class GpuUnavailableException : InvalidOperationException
{
    public GpuUnavailableException()
    {
    }

    public GpuUnavailableException(string message)
        : base(message)
    {
    }

    public GpuUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class GpuAdapters
{
    public static IDXGIAdapter1 Choose(IDXGIFactory2 factory, string adapterName, List<IDisposable> owned)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(adapterName);
        ArgumentNullException.ThrowIfNull(owned);
        var candidates = new List<IDXGIAdapter1>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            owned.Add(adapter);
            if ((adapter.Description1.Flags & AdapterFlags.Software) == 0)
            {
                candidates.Add(adapter);
            }
        }

        var byName = adapterName.Length > 0
            ? candidates.FirstOrDefault(a => a.Description1.Description.Contains(adapterName, StringComparison.OrdinalIgnoreCase))
            : null;
        return byName
            ?? candidates.OrderByDescending(a => a.Description1.DedicatedVideoMemory).FirstOrDefault()
            ?? throw new GpuUnavailableException("Nenhuma GPU compatível com Direct3D 11.");
    }
}
