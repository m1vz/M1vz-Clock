using System.Runtime.InteropServices;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Wmi;
using Microsoft.Extensions.Logging;
using SharpGen.Runtime;

namespace M1vClock.Hardware.Gpu;

public interface ISystemLibraries
{
    bool Exists(string fileName);
}

public sealed class WindowsSystemLibraries : ISystemLibraries
{
    public bool Exists(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return File.Exists(Path.Combine(Environment.SystemDirectory, fileName));
    }
}

public sealed class GpuCollector(IWmiReader wmi, IDxgiAdapterSource dxgi, ISystemLibraries libraries, GpuSpecDatabase database, ILogger<GpuCollector> logger)
{
    private const ulong BytesPerGigabyte = 1UL << 30;
    private const string WhqlSigner = "Windows Hardware Compatibility";

    private static readonly string[] VideoProperties = ["Name", "PNPDeviceID", "DriverVersion", "DriverDate", "AdapterRAM"];
    private static readonly string[] SignedProperties = ["DeviceID", "IsSigned", "Signer"];

    public IReadOnlyList<GpuInfo> Read()
    {
        var rows = wmi.Query(new WmiQuery("Win32_VideoController", VideoProperties));
        if (rows.Count == 0)
        {
            throw new HardwareReadException("O Windows não informou nenhuma placa de vídeo.");
        }

        var adapters = ReadAdapters();
        var signed = ReadSignedDrivers();
        var taken = new HashSet<int>();
        var gpus = rows.Select(row => Build(row, adapters, signed, taken)).ToList();
        return gpus.OrderByDescending(g => g.VideoMemoryBytes).ToList();
    }

    public static ulong MarketedGigabytes(ulong bytes) => bytes < BytesPerGigabyte ? 0 : (ulong)Math.Ceiling(bytes / (double)BytesPerGigabyte - 0.15);

    private GpuInfo Build(WmiRow row, IReadOnlyList<DxgiAdapter> adapters, IReadOnlyList<WmiRow> signed, HashSet<int> taken)
    {
        var name = row.Text("Name") ?? "GPU";
        var pnp = row.Text("PNPDeviceID") ?? string.Empty;
        var pci = PciId.Parse(pnp);
        var spec = pci is null ? null : database.Find(pci.VendorId, pci.DeviceId, name);
        var adapterIndex = pci is null ? -1 : FindAdapter(adapters, pci, taken);
        var adapter = adapterIndex >= 0 ? adapters[adapterIndex] : null;
        if (adapterIndex >= 0)
        {
            taken.Add(adapterIndex);
        }

        var (signature, signer) = SignatureOf(pnp, signed);
        var vram = adapter?.DedicatedVideoBytes > 0 ? adapter.DedicatedVideoBytes : row.Size("AdapterRAM");
        var level = adapter?.FeatureLevel;
        return new GpuInfo(
            name,
            pci is null ? "—" : database.VendorName(pci.VendorId),
            pci,
            spec,
            vram,
            adapter?.SharedSystemBytes ?? 0,
            row.Text("DriverVersion") ?? "—",
            row.Date("DriverDate") is { } date ? DateOnly.FromDateTime(date) : null,
            signature,
            signer,
            level,
            ApisOf(pci, level),
            spec is null ? null : database.FeaturesOf(spec.Architecture));
    }

    private static int FindAdapter(IReadOnlyList<DxgiAdapter> adapters, PciId pci, HashSet<int> taken)
    {
        for (var i = 0; i < adapters.Count; i++)
        {
            var candidate = adapters[i];
            if (!taken.Contains(i) && candidate.VendorId.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) == pci.VendorId && candidate.DeviceId.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) == pci.DeviceId)
            {
                return i;
            }
        }

        return -1;
    }

    private static (DriverSignature Signature, string? Signer) SignatureOf(string pnp, IReadOnlyList<WmiRow> signed)
    {
        var row = signed.FirstOrDefault(r => string.Equals(r.Text("DeviceID"), pnp, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return (DriverSignature.Unknown, null);
        }

        if (row.Bool("IsSigned") != true)
        {
            return (DriverSignature.NotSigned, null);
        }

        var signer = row.Text("Signer");
        return signer?.Contains(WhqlSigner, StringComparison.OrdinalIgnoreCase) == true
            ? (DriverSignature.Whql, signer)
            : (DriverSignature.VendorSigned, signer);
    }

    private List<GpuApi> ApisOf(PciId? pci, string? featureLevel)
    {
        var nvidia = pci?.VendorId == "10DE";
        var apis = new List<GpuApi>();
        if (nvidia && libraries.Exists("nvcuda.dll"))
        {
            apis.Add(GpuApi.Cuda);
        }

        if (libraries.Exists("OpenCL.dll"))
        {
            apis.Add(GpuApi.OpenCl);
        }

        if (libraries.Exists("vulkan-1.dll"))
        {
            apis.Add(GpuApi.Vulkan);
        }

        if (featureLevel is not null)
        {
            apis.Add(GpuApi.DirectCompute);
            apis.Add(GpuApi.DirectMl);
        }

        if (nvidia)
        {
            apis.Add(GpuApi.PhysX);
        }

        return apis;
    }

    private IReadOnlyList<DxgiAdapter> ReadAdapters()
    {
        try
        {
            return dxgi.Enumerate();
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or SharpGenException or InvalidOperationException or EntryPointNotFoundException)
        {
            HardwareLog.OptionalSourceFailed(logger, ex, "DXGI");
            return [];
        }
    }

    private IReadOnlyList<WmiRow> ReadSignedDrivers()
    {
        try
        {
            return wmi.Query(new WmiQuery("Win32_PnPSignedDriver", SignedProperties));
        }
        catch (UnauthorizedAccessException)
        {
            HardwareLog.OptionalSourceDenied(logger, "Win32_PnPSignedDriver");
            return [];
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or COMException or InvalidOperationException or TimeoutException)
        {
            HardwareLog.OptionalSourceFailed(logger, ex, "Win32_PnPSignedDriver");
            return [];
        }
    }
}
