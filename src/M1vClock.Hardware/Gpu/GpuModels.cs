using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using M1vClock.Core.Results;

namespace M1vClock.Hardware.Gpu;

public sealed partial record PciId(string VendorId, string DeviceId, string SubsystemVendorId, string SubsystemDeviceId, string Revision)
{
    public string Key => $"{VendorId}:{DeviceId}";

    public static PciId? Parse(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return null;
        }

        var match = Pattern().Match(pnpDeviceId);
        if (!match.Success)
        {
            return null;
        }

        return new PciId(
            match.Groups[1].Value.ToUpperInvariant(),
            match.Groups[2].Value.ToUpperInvariant(),
            match.Groups[4].Value.ToUpperInvariant(),
            match.Groups[3].Value.ToUpperInvariant(),
            match.Groups[5].Value.ToUpperInvariant());
    }

    [GeneratedRegex(@"VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})(?:&SUBSYS_([0-9A-F]{4})([0-9A-F]{4}))?(?:&REV_([0-9A-F]{2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}

public sealed record GpuSpec(
    [property: JsonPropertyName("chip")] string Chip,
    [property: JsonPropertyName("arch")] string Architecture,
    [property: JsonPropertyName("process")] string Process,
    [property: JsonPropertyName("dieMm2")] int DieMm2,
    [property: JsonPropertyName("transistorsBillions")] double TransistorsBillions,
    [property: JsonPropertyName("shaders")] int Shaders,
    [property: JsonPropertyName("rops")] int Rops,
    [property: JsonPropertyName("tmus")] int Tmus,
    [property: JsonPropertyName("memoryType")] string MemoryType,
    [property: JsonPropertyName("baseMhz")] int BaseMhz,
    [property: JsonPropertyName("boostMhz")] int BoostMhz,
    [property: JsonPropertyName("tdpWatts")] int TdpWatts,
    [property: JsonPropertyName("release")] string Release,
    [property: JsonPropertyName("nameMatch")] string? NameMatch)
{
    public bool UsesSystemMemory => MemoryType.StartsWith("Memória do sistema", StringComparison.Ordinal);

    public double Fp32Teraflops(int shaders) => shaders * 2.0 * BoostMhz / 1_000_000.0;

    public double PixelFillrate => Rops * BoostMhz / 1000.0;

    public double TextureFillrate => Tmus * BoostMhz / 1000.0;
}

public enum DriverSignature
{
    Unknown,
    NotSigned,
    Whql,
    VendorSigned,
}

public enum GpuApi
{
    Cuda,
    OpenCl,
    Vulkan,
    DirectCompute,
    DirectMl,
    PhysX,
}

public sealed record DxgiAdapter(
    string Description,
    uint VendorId,
    uint DeviceId,
    ulong DedicatedVideoBytes,
    ulong DedicatedSystemBytes,
    ulong SharedSystemBytes,
    string? FeatureLevel);

public sealed record GpuInfo(
    string Name,
    string VendorName,
    PciId? Pci,
    GpuSpec? Spec,
    ulong VideoMemoryBytes,
    ulong SharedMemoryBytes,
    string DriverVersion,
    DateOnly? DriverDate,
    DriverSignature Signature,
    string? Signer,
    string? FeatureLevel,
    IReadOnlyList<GpuApi> Apis,
    string? ArchitectureFeatures)
{
    public bool IsIntegrated => Spec?.UsesSystemMemory == true;

    public int ShaderCount => Spec?.Shaders ?? 0;
}

public sealed record GpuReport(ReadResult<IReadOnlyList<GpuInfo>> Gpus);
