using System.Globalization;
using M1vClock.App.Formatting;
using M1vClock.App.Localization;
using M1vClock.Hardware.Gpu;

namespace M1vClock.App.Pages.Hardware;

public static class GpuRows
{
    private const string Nvidia = "10DE";
    private const ulong BytesPerGigabyte = 1UL << 30;
    private const ulong BytesPerMegabyte = 1UL << 20;

    public static IReadOnlyList<InfoRowViewModel> Identification(GpuInfo gpu, GpuSpecDatabase database)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(database);
        var spec = gpu.Spec;
        var pci = gpu.Pci;
        return
        [
            Row("Hw.Gpu.Name", gpu.Name),
            Row("Hw.Gpu.Vendor", gpu.VendorName),
            Row("Hw.Gpu.Chip", spec?.Chip ?? Units.Missing),
            Row("Hw.Gpu.Arch", spec?.Architecture ?? Units.Missing),
            Row("Hw.Gpu.Revision", Units.OrMissing(pci?.Revision)),
            Row("Hw.Gpu.Board", pci is { SubsystemVendorId.Length: > 0 } ? database.SubvendorName(pci.SubsystemVendorId) : Units.Missing),
            Row("Hw.Gpu.DeviceId", DeviceId(pci)),
            Row("Hw.Gpu.Process", spec?.Process ?? Units.Missing),
            Row("Hw.Gpu.Die", spec is null ? Units.Missing : Loc.Format("Hw.Gpu.Die.Value", spec.DieMm2.ToString("N0", Loc.Culture))),
            Row("Hw.Gpu.Transistors", spec is null ? Units.Missing : Loc.Format("Hw.Gpu.Transistors.Value", spec.TransistorsBillions.ToString("0.#", Loc.Culture))),
            Row("Hw.Gpu.Release", spec?.Release ?? Units.Missing),
        ];
    }

    public static IReadOnlyList<InfoRowViewModel> ProcessingUnits(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        var spec = gpu.Spec;
        if (spec is null)
        {
            return [new InfoRowViewModel(Loc.Get("Hw.Gpu.NoData"), Loc.Get("Hw.Gpu.NoData.Detail"))];
        }

        var nvidia = gpu.Pci?.VendorId == Nvidia;
        var perUnit = nvidia ? 128 : 64;
        var rows = new List<InfoRowViewModel>
        {
            Row(nvidia ? "Hw.Gpu.Shaders.Nvidia" : "Hw.Gpu.Shaders.Amd", spec.Shaders.ToString("N0", Loc.Culture)),
            Row(nvidia ? "Hw.Gpu.Units.Nvidia" : "Hw.Gpu.Units.Amd", (spec.Shaders / perUnit).ToString(Loc.Culture)),
        };
        if (!nvidia && spec.Architecture.StartsWith("RDNA", StringComparison.Ordinal) && spec.Architecture != "RDNA 1")
        {
            rows.Add(Row("Hw.Gpu.RayAccelerators", (spec.Shaders / perUnit).ToString(Loc.Culture)));
        }

        rows.Add(Row("Hw.Gpu.RopsTmus", Loc.Format("Hw.Gpu.RopsTmus.Value", spec.Rops, spec.Tmus)));
        rows.Add(Row("Hw.Gpu.PixelFill", Loc.Format("Hw.Gpu.PixelFill.Value", spec.PixelFillrate.ToString("0.0", Loc.Culture))));
        rows.Add(Row("Hw.Gpu.TexelFill", Loc.Format("Hw.Gpu.TexelFill.Value", spec.TextureFillrate.ToString("0.0", Loc.Culture))));
        rows.Add(Row("Hw.Gpu.Fp32", Loc.Format("Hw.Gpu.Fp32.Value", spec.Fp32Teraflops(spec.Shaders).ToString("0.00", Loc.Culture))));
        return rows;
    }

    public static IReadOnlyList<InfoRowViewModel> Memory(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        var rows = new List<InfoRowViewModel>();
        var vram = gpu.VideoMemoryBytes;
        if (gpu.IsIntegrated || vram < BytesPerGigabyte)
        {
            rows.Add(Row(gpu.IsIntegrated ? "Hw.Gpu.Mem.Reserved" : "Hw.Gpu.Mem.Size", Units.Bytes(vram)));
        }
        else
        {
            var marketed = GpuCollector.MarketedGigabytes(vram);
            rows.Add(Row("Hw.Gpu.Mem.Size", Loc.Format("Hw.Gpu.Mem.Size.Value", marketed.ToString(Loc.Culture), (vram / BytesPerMegabyte).ToString("N0", Loc.Culture))));
        }

        if (gpu.SharedMemoryBytes > 0)
        {
            rows.Add(Row("Hw.Gpu.Mem.Shared", Units.Bytes(gpu.SharedMemoryBytes)));
        }

        rows.Add(Row("Hw.Gpu.Mem.Type", gpu.Spec?.MemoryType ?? Units.Missing));
        return rows;
    }

    public static IReadOnlyList<InfoRowViewModel> Clocks(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        if (gpu.Spec is not { } spec)
        {
            return [new InfoRowViewModel(Loc.Get("Hw.Gpu.NoData"), Units.Missing)];
        }

        var rows = new List<InfoRowViewModel>
        {
            Row("Hw.Gpu.Clock.Base", Loc.Format("Hw.Gpu.Clock.Value", spec.BaseMhz.ToString(Loc.Culture))),
            Row("Hw.Gpu.Clock.Boost", Loc.Format("Hw.Gpu.Clock.Value", spec.BoostMhz.ToString(Loc.Culture))),
        };
        if (spec.TdpWatts > 0)
        {
            rows.Add(Row("Hw.Gpu.Tdp", Loc.Format("Hw.Gpu.Tdp.Value", spec.TdpWatts.ToString(Loc.Culture))));
        }

        return rows;
    }

    public static IReadOnlyList<InfoRowViewModel> Driver(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        return
        [
            Row("Hw.Gpu.Driver.Version", gpu.DriverVersion),
            Row("Hw.Gpu.Driver.Date", Units.Date(gpu.DriverDate)),
            Row("Hw.Gpu.Driver.Signature", SignatureText(gpu)),
        ];
    }

    public static IReadOnlyList<InfoRowViewModel> Technologies(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        var rows = new List<InfoRowViewModel>
        {
            Row("Hw.Gpu.Tech.DirectX", DirectXText(gpu.FeatureLevel)),
            Row("Hw.Gpu.Tech.Apis", gpu.Apis.Count > 0 ? string.Join(" · ", gpu.Apis.Select(ApiText)) : Units.Missing),
        };
        if (gpu.ArchitectureFeatures is { } features)
        {
            rows.Add(Row("Hw.Gpu.Tech.Features", features));
        }

        return rows;
    }

    public static string DirectXText(string? featureLevel)
    {
        if (string.IsNullOrEmpty(featureLevel))
        {
            return Units.Missing;
        }

        if (featureLevel == "12_2")
        {
            return Loc.Get("Hw.Gpu.Tech.DirectX.Ultimate");
        }

        var major = featureLevel.Split('_')[0];
        return Loc.Format("Hw.Gpu.Tech.DirectX.Level", major, featureLevel.Replace('_', '.'));
    }

    public static string ApiText(GpuApi api) => api switch
    {
        GpuApi.Cuda => "CUDA",
        GpuApi.OpenCl => "OpenCL",
        GpuApi.Vulkan => "Vulkan",
        GpuApi.DirectCompute => "DirectCompute",
        GpuApi.DirectMl => "DirectML",
        _ => "PhysX",
    };

    public static string SignatureText(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        return gpu.Signature switch
        {
            DriverSignature.Whql => Loc.Get("Hw.Gpu.Sig.Whql"),
            DriverSignature.VendorSigned => Loc.Format("Hw.Gpu.Sig.Signed", gpu.Signer ?? Units.Missing),
            DriverSignature.NotSigned => Loc.Get("Hw.Gpu.Sig.None"),
            _ => Units.Missing,
        };
    }

    public static string Subtitle(GpuInfo gpu, GpuSpecDatabase database)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(database);
        var spec = gpu.Spec;
        var memory = gpu.VideoMemoryBytes >= BytesPerGigabyte && !gpu.IsIntegrated
            ? $"{GpuCollector.MarketedGigabytes(gpu.VideoMemoryBytes).ToString(CultureInfo.InvariantCulture)} GB {(spec?.MemoryType.StartsWith("GDDR", StringComparison.Ordinal) == true ? spec.MemoryType : string.Empty)}".Trim()
            : string.Empty;
        var board = gpu.Pci is { SubsystemVendorId.Length: > 0 } pci ? database.SubvendorName(pci.SubsystemVendorId) : string.Empty;
        return string.Join(" · ", new[] { spec?.Chip ?? string.Empty, spec?.Architecture ?? string.Empty, spec?.Process ?? string.Empty, memory, board }.Where(p => p.Length > 0));
    }

    private static string DeviceId(PciId? pci)
    {
        if (pci is null)
        {
            return Units.Missing;
        }

        var main = $"{pci.VendorId} {pci.DeviceId}";
        return pci.SubsystemVendorId.Length > 0 ? $"{main} · {pci.SubsystemVendorId} {pci.SubsystemDeviceId}" : main;
    }

    private static InfoRowViewModel Row(string labelKey, string value) => new(Loc.Get(labelKey), value);
}
