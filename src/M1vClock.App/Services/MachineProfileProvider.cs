using M1vClock.App.Hosting;
using M1vClock.Benchmarks.Results;
using M1vClock.Core.Results;
using M1vClock.Hardware;
using M1vClock.Hardware.Gpu;
using M1vClock.Hardware.Platform;
using M1vClock.Hardware.Ram;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Services;

public sealed class MachineProfileProvider
{
    private const double BytesPerGigabyte = 1024.0 * 1024 * 1024;

    private readonly IPlatformService _platform;
    private readonly IGpuService _gpu;
    private readonly IRamService _ram;
    private readonly AppInfo _info;
    private readonly ILogger<MachineProfileProvider> _logger;
    private BenchmarkMachine? _cached;

    public MachineProfileProvider(IPlatformService platform, IGpuService gpu, IRamService ram, AppInfo info, ILogger<MachineProfileProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(ram);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(logger);
        _platform = platform;
        _gpu = gpu;
        _ram = ram;
        _info = info;
        _logger = logger;
    }

    public async Task<BenchmarkMachine> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var platformTask = _platform.ReadAsync(cancellationToken);
        var gpuTask = _gpu.ReadAsync(cancellationToken);
        var ramTask = _ram.ReadAsync(cancellationToken);
        try
        {
            await Task.WhenAll(platformTask, gpuTask, ramTask).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HardwareReadException or InvalidOperationException)
        {
            AppLog.MachineProfileFailed(_logger, ex);
        }

        var machine = Build(
            platformTask.IsCompletedSuccessfully ? platformTask.Result : null,
            gpuTask.IsCompletedSuccessfully ? gpuTask.Result : null,
            ramTask.IsCompletedSuccessfully ? ramTask.Result : null);
        _cached = machine;
        return machine;
    }

    public BenchmarkMachine Build(PlatformReport? platform, GpuReport? gpu, RamReport? ram)
    {
        var cpu = string.Empty;
        var logical = Environment.ProcessorCount;
        if (platform is not null && platform.Cpus.TryGet(out var cpus) && cpus.Count > 0)
        {
            cpu = cpus[0].Name.Trim();
            var threads = cpus.Sum(c => c.Threads);
            if (threads > 0)
            {
                logical = threads;
            }
        }

        var os = string.Empty;
        if (platform is not null && platform.Os.TryGet(out var info))
        {
            os = $"{info.Name} {info.FullBuild}".Trim();
        }

        var gpuName = string.Empty;
        if (gpu is not null && gpu.Gpus.TryGet(out var gpus) && gpus.Count > 0)
        {
            gpuName = gpus.OrderBy(g => g.IsIntegrated).ThenByDescending(g => g.VideoMemoryBytes).First().Name.Trim();
        }

        var memory = 0.0;
        if (ram is not null && ram.Usage.TryGet(out var usage))
        {
            memory = Math.Round(usage.TotalBytes / BytesPerGigabyte, 1);
        }

        return new BenchmarkMachine(cpu, gpuName, logical, memory, os, _info.Version);
    }
}
