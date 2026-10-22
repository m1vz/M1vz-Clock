using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace M1vClock.Hardware.Gpu;

public interface IDxgiAdapterSource
{
    IReadOnlyList<DxgiAdapter> Enumerate();
}

public sealed class VorticeDxgiAdapterSource : IDxgiAdapterSource
{
    private static readonly (FeatureLevel Level, string Name)[] Levels =
    [
        (FeatureLevel.Level_12_2, "12_2"),
        (FeatureLevel.Level_12_1, "12_1"),
        (FeatureLevel.Level_12_0, "12_0"),
        (FeatureLevel.Level_11_1, "11_1"),
        (FeatureLevel.Level_11_0, "11_0"),
    ];

    public IReadOnlyList<DxgiAdapter> Enumerate()
    {
        var result = new List<DxgiAdapter>();
        if (DXGI.CreateDXGIFactory1<IDXGIFactory1>(out var factory).Failure || factory is null)
        {
            return result;
        }

        using (factory)
        {
            for (uint index = 0; factory.EnumAdapters1(index, out var adapter).Success; index++)
            {
                using (adapter)
                {
                    var description = adapter.Description1;
                    if ((description.Flags & AdapterFlags.Software) != 0)
                    {
                        continue;
                    }

                    result.Add(new DxgiAdapter(
                        description.Description,
                        description.VendorId,
                        description.DeviceId,
                        (ulong)description.DedicatedVideoMemory,
                        (ulong)description.DedicatedSystemMemory,
                        (ulong)description.SharedSystemMemory,
                        HighestFeatureLevel(adapter)));
                }
            }
        }

        return result;
    }

    private static string? HighestFeatureLevel(IDXGIAdapter1 adapter)
    {
        foreach (var (level, name) in Levels)
        {
            if (D3D12.IsSupported(adapter, level))
            {
                return name;
            }
        }

        return null;
    }
}
