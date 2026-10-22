using Microsoft.Extensions.Logging;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace M1vClock.Benchmarks.Gpu;

public enum RayTracingStatus
{
    Supported,
    NoHardwareRayTracing,
    Direct3D12Unavailable,
    NoGpu,
}

public sealed record RayTracingSupport(RayTracingStatus Status, string Adapter, string Tier);

public static class RayTracingCheck
{
    public static RayTracingSupport Check(string adapterName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(adapterName);
        ArgumentNullException.ThrowIfNull(logger);
        var owned = new List<IDisposable>();
        try
        {
            var factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>();
            owned.Add(factory);
            IDXGIAdapter1 adapter;
            try
            {
                adapter = GpuAdapters.Choose(factory, adapterName, owned);
            }
            catch (GpuUnavailableException ex)
            {
                GpuLog.NoGpuForRayTracing(logger, ex);
                return new RayTracingSupport(RayTracingStatus.NoGpu, string.Empty, string.Empty);
            }

            var name = adapter.Description1.Description;
            var device = D3D12.D3D12CreateDevice<ID3D12Device5>(adapter, FeatureLevel.Level_12_0);
            owned.Add(device);
            var tier = device.Options5.RaytracingTier;
            return new RayTracingSupport(
                tier >= RaytracingTier.Tier1_1 ? RayTracingStatus.Supported : RayTracingStatus.NoHardwareRayTracing,
                name,
                tier.ToString());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException or InsufficientMemoryException)
        {
            GpuLog.RayTracingCheckFailed(logger, ex);
            return new RayTracingSupport(RayTracingStatus.Direct3D12Unavailable, string.Empty, string.Empty);
        }
        finally
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                owned[i].Dispose();
            }
        }
    }
}

public static class GpuSupport
{
    public static bool HasHardwareAdapter()
    {
        var owned = new List<IDisposable>();
        try
        {
            var factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
            owned.Add(factory);
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                owned.Add(adapter);
                if ((adapter.Description1.Flags & AdapterFlags.Software) == 0)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                owned[i].Dispose();
            }
        }
    }
}
