using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M1vClock.Hardware.Gpu;

public sealed class GpuSpecDatabase
{
    private const string ResourceName = "M1vClock.Hardware.Gpu.GpuSpecs.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static readonly Lazy<GpuSpecDatabase> Shared = new(LoadEmbedded);

    private readonly Dictionary<string, IReadOnlyList<GpuSpec>> _specs;
    private readonly Dictionary<string, string> _vendors;
    private readonly Dictionary<string, string> _subvendors;
    private readonly Dictionary<string, string> _features;

    private GpuSpecDatabase(GpuSpecFile file)
    {
        _specs = file.Specs.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<GpuSpec>)pair.Value, StringComparer.OrdinalIgnoreCase);
        _vendors = new Dictionary<string, string>(file.Vendors, StringComparer.OrdinalIgnoreCase);
        _subvendors = new Dictionary<string, string>(file.Subvendors, StringComparer.OrdinalIgnoreCase);
        _features = new Dictionary<string, string>(file.Features, StringComparer.OrdinalIgnoreCase);
    }

    public static GpuSpecDatabase Default => Shared.Value;

    public IReadOnlyCollection<string> Ids => _specs.Keys;

    public IEnumerable<GpuSpec> All => _specs.Values.SelectMany(list => list);

    public IReadOnlyDictionary<string, string> Features => _features;

    public static GpuSpecDatabase FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var file = JsonSerializer.Deserialize<GpuSpecFile>(json, Options) ?? throw new InvalidDataException("O banco de GPUs está vazio.");
        return new GpuSpecDatabase(file);
    }

    public GpuSpec? Find(string vendorId, string deviceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!_specs.TryGetValue($"{vendorId}:{deviceId}", out var specs) || specs.Count == 0)
        {
            return null;
        }

        return specs.FirstOrDefault(s => s.NameMatch is not null && name.Contains(s.NameMatch, StringComparison.OrdinalIgnoreCase)) ?? specs[0];
    }

    public string VendorName(string vendorId) => _vendors.TryGetValue(vendorId, out var name) ? name : vendorId;

    public string SubvendorName(string subsystemVendorId) => _subvendors.TryGetValue(subsystemVendorId, out var name) ? name : subsystemVendorId;

    public string? FeaturesOf(string architecture) => _features.GetValueOrDefault(architecture);

    private static GpuSpecDatabase LoadEmbedded()
    {
        using var stream = typeof(GpuSpecDatabase).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Recurso {ResourceName} não encontrado.");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    private sealed class GpuSpecFile
    {
        [JsonPropertyName("vendors")]
        public Dictionary<string, string> Vendors { get; init; } = [];

        [JsonPropertyName("subvendors")]
        public Dictionary<string, string> Subvendors { get; init; } = [];

        [JsonPropertyName("features")]
        public Dictionary<string, string> Features { get; init; } = [];

        [JsonPropertyName("specs")]
        public Dictionary<string, List<GpuSpec>> Specs { get; init; } = [];
    }
}
