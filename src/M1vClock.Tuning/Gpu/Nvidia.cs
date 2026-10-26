using System.Runtime.InteropServices;
using System.Text;
using M1vClock.Core.Native;
using Microsoft.Extensions.Logging;

namespace M1vClock.Tuning.Gpu;

public enum NvmlReturn
{
    Success = 0,
    Uninitialized = 1,
    InvalidArgument = 2,
    NotSupported = 3,
    NoPermission = 4,
    NotFound = 6,
    GpuIsLost = 15,
    Unknown = 999,
}

public interface INvmlApi
{
    NvmlReturn Init();

    void Shutdown();

    NvmlReturn DeviceCount(out uint count);

    NvmlReturn Handle(uint index, out IntPtr device);

    NvmlReturn Name(IntPtr device, out string name);

    NvmlReturn PowerConstraints(IntPtr device, out uint minimumMilliwatts, out uint maximumMilliwatts);

    NvmlReturn PowerLimit(IntPtr device, out uint milliwatts);

    NvmlReturn PowerDefault(IntPtr device, out uint milliwatts);

    NvmlReturn SetPowerLimit(IntPtr device, uint milliwatts);

    NvmlReturn MaxGraphicsClock(IntPtr device, out uint megahertz);

    NvmlReturn GraphicsClock(IntPtr device, out uint megahertz);

    NvmlReturn LockGraphicsClocks(IntPtr device, uint minimumMegahertz, uint maximumMegahertz);

    NvmlReturn ResetGraphicsClocks(IntPtr device);
}

public sealed record NvidiaRequest(int PowerWatts, int? ClockCapMegahertz);

public enum NvidiaApplyStatus
{
    Applied,
    Partial,
    Refused,
    OutOfRange,
    NoDevice,
    LibraryMissing,
}

public sealed record NvidiaApplyResult(NvidiaApplyStatus Status, NvmlReturn Power, NvmlReturn Clock);

public interface IGpuTuner
{
    NvidiaApplyResult Apply(int index, NvidiaRequest request);

    NvidiaApplyResult RestoreDefaults(int index);
}

public sealed class NvidiaTuner(Func<INvmlApi?> open, ILogger<NvidiaTuner> logger) : IGpuTuningReader, IGpuTuner
{
    public const int MinimumClockCap = 300;
    private const uint FloorClock = 210;

    private readonly Lazy<INvmlApi?> _api = new(open);

    public GpuVendor Vendor => GpuVendor.Nvidia;

    public GpuTuningReading Read()
    {
        if (_api.Value is not { } api)
        {
            return GpuTuningReading.Missing;
        }

        var init = api.Init();
        if (init != NvmlReturn.Success)
        {
            return new GpuTuningReading(GpuReadStatus.Failed, [], "nvmlInit: " + init);
        }

        try
        {
            if (api.DeviceCount(out var count) != NvmlReturn.Success || count == 0)
            {
                return new GpuTuningReading(GpuReadStatus.NoDevice, []);
            }

            var gpus = new List<GpuTuningInfo>();
            for (uint i = 0; i < count; i++)
            {
                if (api.Handle(i, out var device) == NvmlReturn.Success)
                {
                    gpus.Add(Describe(api, device));
                }
            }

            return new GpuTuningReading(gpus.Count == 0 ? GpuReadStatus.NoDevice : GpuReadStatus.Read, gpus);
        }
        finally
        {
            api.Shutdown();
        }
    }

    public NvidiaApplyResult Apply(int index, NvidiaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WithDevice(index, (api, device) =>
        {
            if (api.PowerConstraints(device, out var minimum, out var maximum) != NvmlReturn.Success)
            {
                return new NvidiaApplyResult(NvidiaApplyStatus.Refused, NvmlReturn.NotSupported, NvmlReturn.NotSupported);
            }

            var milliwatts = (long)request.PowerWatts * 1000;
            var maxClock = api.MaxGraphicsClock(device, out var top) == NvmlReturn.Success ? top : 0u;
            if (milliwatts < minimum || milliwatts > maximum
                || (request.ClockCapMegahertz is { } cap && (cap < MinimumClockCap || (maxClock > 0 && cap > maxClock))))
            {
                return new NvidiaApplyResult(NvidiaApplyStatus.OutOfRange, NvmlReturn.InvalidArgument, NvmlReturn.InvalidArgument);
            }

            var power = api.SetPowerLimit(device, (uint)milliwatts);
            var clock = request.ClockCapMegahertz is { } limit
                ? api.LockGraphicsClocks(device, FloorClock, (uint)limit)
                : api.ResetGraphicsClocks(device);
            TuningLog.NvidiaApplied(logger, index, power, clock);
            return new NvidiaApplyResult(Combine(power, clock), power, clock);
        });
    }

    public NvidiaApplyResult RestoreDefaults(int index) => WithDevice(index, (api, device) =>
    {
        var power = api.PowerDefault(device, out var standard) == NvmlReturn.Success ? api.SetPowerLimit(device, standard) : NvmlReturn.NotSupported;
        var clock = api.ResetGraphicsClocks(device);
        TuningLog.NvidiaRestored(logger, index, power, clock);
        return new NvidiaApplyResult(Combine(power, clock), power, clock);
    });

    private static NvidiaApplyStatus Combine(NvmlReturn power, NvmlReturn clock) => (power, clock) switch
    {
        (NvmlReturn.Success, NvmlReturn.Success) => NvidiaApplyStatus.Applied,
        (NvmlReturn.Success, _) or (_, NvmlReturn.Success) => NvidiaApplyStatus.Partial,
        _ => NvidiaApplyStatus.Refused,
    };

    private static GpuTuningInfo Describe(INvmlApi api, IntPtr device)
    {
        var name = api.Name(device, out var text) == NvmlReturn.Success ? text : "NVIDIA";
        TuningRange? power = api.PowerConstraints(device, out var minimum, out var maximum) == NvmlReturn.Success
            ? new TuningRange((int)(minimum / 1000), (int)(maximum / 1000), 1)
            : null;
        int? current = api.PowerLimit(device, out var limit) == NvmlReturn.Success ? (int)(limit / 1000) : null;
        int? standard = api.PowerDefault(device, out var defaultLimit) == NvmlReturn.Success ? (int)(defaultLimit / 1000) : null;
        TuningRange? clocks = api.MaxGraphicsClock(device, out var top) == NvmlReturn.Success ? new TuningRange(MinimumClockCap, (int)top, 15) : null;
        int? clock = api.GraphicsClock(device, out var now) == NvmlReturn.Success ? (int)now : null;
        return new GpuTuningInfo(GpuVendor.Nvidia, name, clocks is not null, power is not null, current == standard ? true : null, clocks, clock, null, null, power, current, standard, PowerUnit.Watts);
    }

    private NvidiaApplyResult WithDevice(int index, Func<INvmlApi, IntPtr, NvidiaApplyResult> action)
    {
        if (_api.Value is not { } api)
        {
            return new NvidiaApplyResult(NvidiaApplyStatus.LibraryMissing, NvmlReturn.Uninitialized, NvmlReturn.Uninitialized);
        }

        if (api.Init() != NvmlReturn.Success)
        {
            return new NvidiaApplyResult(NvidiaApplyStatus.LibraryMissing, NvmlReturn.Uninitialized, NvmlReturn.Uninitialized);
        }

        try
        {
            return index < 0 || api.Handle((uint)index, out var device) != NvmlReturn.Success
                ? new NvidiaApplyResult(NvidiaApplyStatus.NoDevice, NvmlReturn.NotFound, NvmlReturn.NotFound)
                : action(api, device);
        }
        finally
        {
            api.Shutdown();
        }
    }
}

public sealed unsafe class NvmlLibrary : INvmlApi, IDisposable
{
    public const string LibraryName = "nvml.dll";
    private const uint GraphicsClockType = 0;

    private readonly IntPtr _library;
    private readonly delegate* unmanaged<int> _init;
    private readonly delegate* unmanaged<int> _shutdown;
    private readonly delegate* unmanaged<uint*, int> _count;
    private readonly delegate* unmanaged<uint, IntPtr*, int> _handle;
    private readonly delegate* unmanaged<IntPtr, byte*, uint, int> _name;
    private readonly delegate* unmanaged<IntPtr, uint*, uint*, int> _constraints;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _limit;
    private readonly delegate* unmanaged<IntPtr, uint*, int> _default;
    private readonly delegate* unmanaged<IntPtr, uint, int> _setLimit;
    private readonly delegate* unmanaged<IntPtr, uint, uint*, int> _maxClock;
    private readonly delegate* unmanaged<IntPtr, uint, uint*, int> _clock;
    private readonly delegate* unmanaged<IntPtr, uint, uint, int> _lock;
    private readonly delegate* unmanaged<IntPtr, int> _reset;

    private NvmlLibrary(IntPtr library)
    {
        _library = library;
        _init = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlInit_v2");
        _shutdown = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlShutdown");
        _count = (delegate* unmanaged<uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2");
        _handle = (delegate* unmanaged<uint, IntPtr*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2");
        _name = (delegate* unmanaged<IntPtr, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetName");
        _constraints = (delegate* unmanaged<IntPtr, uint*, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetPowerManagementLimitConstraints");
        _limit = (delegate* unmanaged<IntPtr, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetPowerManagementLimit");
        _default = (delegate* unmanaged<IntPtr, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetPowerManagementDefaultLimit");
        _setLimit = (delegate* unmanaged<IntPtr, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceSetPowerManagementLimit");
        _maxClock = (delegate* unmanaged<IntPtr, uint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetMaxClockInfo");
        _clock = (delegate* unmanaged<IntPtr, uint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetClockInfo");
        _lock = (delegate* unmanaged<IntPtr, uint, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceSetGpuLockedClocks");
        _reset = (delegate* unmanaged<IntPtr, int>)NativeLibrary.GetExport(library, "nvmlDeviceResetGpuLockedClocks");
    }

    public static INvmlApi? TryOpen(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, LibraryName)))
        {
            return null;
        }

        try
        {
            return new NvmlLibrary(NativeLibraryPolicy.LoadFromSystem32(LibraryName));
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            TuningLog.LibraryUnavailable(logger, ex, LibraryName);
            return null;
        }
    }

    public NvmlReturn Init() => Map(_init());

    public void Shutdown() => _ = _shutdown();

    public NvmlReturn DeviceCount(out uint count)
    {
        uint value;
        var code = _count(&value);
        count = value;
        return Map(code);
    }

    public NvmlReturn Handle(uint index, out IntPtr device)
    {
        IntPtr value;
        var code = _handle(index, &value);
        device = value;
        return Map(code);
    }

    public NvmlReturn Name(IntPtr device, out string name)
    {
        var buffer = stackalloc byte[96];
        var code = _name(device, buffer, 96);
        name = code == 0 ? Encoding.UTF8.GetString(buffer, new ReadOnlySpan<byte>(buffer, 96).IndexOf((byte)0) is var end and >= 0 ? end : 96) : string.Empty;
        return Map(code);
    }

    public NvmlReturn PowerConstraints(IntPtr device, out uint minimumMilliwatts, out uint maximumMilliwatts)
    {
        uint minimum;
        uint maximum;
        var code = _constraints(device, &minimum, &maximum);
        minimumMilliwatts = minimum;
        maximumMilliwatts = maximum;
        return Map(code);
    }

    public NvmlReturn PowerLimit(IntPtr device, out uint milliwatts) => Read(_limit, device, out milliwatts);

    public NvmlReturn PowerDefault(IntPtr device, out uint milliwatts) => Read(_default, device, out milliwatts);

    public NvmlReturn SetPowerLimit(IntPtr device, uint milliwatts) => Map(_setLimit(device, milliwatts));

    public NvmlReturn MaxGraphicsClock(IntPtr device, out uint megahertz)
    {
        uint value;
        var code = _maxClock(device, GraphicsClockType, &value);
        megahertz = value;
        return Map(code);
    }

    public NvmlReturn GraphicsClock(IntPtr device, out uint megahertz)
    {
        uint value;
        var code = _clock(device, GraphicsClockType, &value);
        megahertz = value;
        return Map(code);
    }

    public NvmlReturn LockGraphicsClocks(IntPtr device, uint minimumMegahertz, uint maximumMegahertz) => Map(_lock(device, minimumMegahertz, maximumMegahertz));

    public NvmlReturn ResetGraphicsClocks(IntPtr device) => Map(_reset(device));

    public void Dispose() => NativeLibrary.Free(_library);

    private static NvmlReturn Read(delegate* unmanaged<IntPtr, uint*, int> function, IntPtr device, out uint result)
    {
        uint value;
        var code = function(device, &value);
        result = value;
        return Map(code);
    }

    private static NvmlReturn Map(int code) => Enum.IsDefined((NvmlReturn)code) ? (NvmlReturn)code : NvmlReturn.Unknown;
}
