using System.Runtime.InteropServices;
using M1vClock.Core.Native;
using Microsoft.Extensions.Logging;

namespace M1vClock.Tuning.Gpu;

public sealed unsafe class AdlxReader(ILogger<AdlxReader> logger) : IGpuTuningReader
{
    public const string LibraryName = "amdadlx64.dll";
    public const int Ok = 0;
    public const int NotSupported = 12;

    private const int SystemGetGpus = 1;
    private const int SystemGetTuningServices = 8;
    private const int Release = 1;
    private const int QueryInterface = 2;
    private const int ListSize = 3;
    private const int ListAtGpu = 11;
    private const int GpuName = 7;
    private const int ServicesIsAtFactory = 4;
    private const int ServicesSupportsManualGraphics = 8;
    private const int ServicesSupportsManualPower = 11;
    private const int ServicesGetManualGraphics = 14;
    private const int ServicesGetManualPower = 17;
    private const int Tuning2MaxFrequencyRange = 6;
    private const int Tuning2MaxFrequency = 7;
    private const int Tuning2VoltageRange = 9;
    private const int Tuning2Voltage = 10;
    private const int Tuning1Ranges = 3;
    private const int PowerRange = 3;
    private const int PowerCurrent = 4;
    private const int Power1Default = 10;

    public GpuVendor Vendor => GpuVendor.Amd;

    public GpuTuningReading Read()
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, LibraryName)))
        {
            return GpuTuningReading.Missing;
        }

        IntPtr library;
        try
        {
            library = NativeLibraryPolicy.LoadFromSystem32(LibraryName);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            TuningLog.LibraryUnavailable(logger, ex, LibraryName);
            return GpuTuningReading.Missing;
        }

        try
        {
            return ReadWith(library);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    internal static int Call(IntPtr self, int slot) => ((delegate* unmanaged<IntPtr, int>)Slot(self, slot))(self);

    private static IntPtr Slot(IntPtr self, int index) => ((IntPtr*)*(IntPtr*)self)[index];

    private static int CallOut(IntPtr self, int slot, out IntPtr result)
    {
        IntPtr value;
        var code = ((delegate* unmanaged<IntPtr, IntPtr*, int>)Slot(self, slot))(self, &value);
        result = value;
        return code;
    }

    private static int CallInt(IntPtr self, int slot, out int result)
    {
        int value;
        var code = ((delegate* unmanaged<IntPtr, int*, int>)Slot(self, slot))(self, &value);
        result = value;
        return code;
    }

    private static int CallRange(IntPtr self, int slot, out TuningRange range)
    {
        var raw = stackalloc int[3];
        var code = ((delegate* unmanaged<IntPtr, int*, int>)Slot(self, slot))(self, raw);
        range = new TuningRange(raw[0], raw[1], raw[2]);
        return code;
    }

    private static bool? GpuFlag(IntPtr services, int slot, IntPtr gpu)
    {
        byte value;
        var code = ((delegate* unmanaged<IntPtr, IntPtr, byte*, int>)Slot(services, slot))(services, gpu, &value);
        return code == Ok ? value != 0 : null;
    }

    private static IntPtr GpuObject(IntPtr services, int slot, IntPtr gpu)
    {
        IntPtr value;
        var code = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Slot(services, slot))(services, gpu, &value);
        return code == Ok ? value : IntPtr.Zero;
    }

    private static IntPtr Query(IntPtr self, string interfaceId)
    {
        IntPtr value;
        int code;
        fixed (char* id = interfaceId)
        {
            code = ((delegate* unmanaged<IntPtr, char*, IntPtr*, int>)Slot(self, QueryInterface))(self, id, &value);
        }

        return code == Ok ? value : IntPtr.Zero;
    }

    private static void Free(IntPtr self)
    {
        if (self != IntPtr.Zero)
        {
            _ = Call(self, Release);
        }
    }

    private static GpuTuningReading ReadWith(IntPtr library)
    {
        if (!NativeLibrary.TryGetExport(library, "ADLXQueryFullVersion", out var queryVersion)
            || !NativeLibrary.TryGetExport(library, "ADLXInitialize", out var initialize)
            || !NativeLibrary.TryGetExport(library, "ADLXTerminate", out var terminate))
        {
            return new GpuTuningReading(GpuReadStatus.Failed, [], "ADLX sem as funções de entrada.");
        }

        ulong version;
        if (((delegate* unmanaged<ulong*, int>)queryVersion)(&version) != Ok)
        {
            return new GpuTuningReading(GpuReadStatus.Failed, [], "ADLXQueryFullVersion falhou.");
        }

        IntPtr system;
        var init = ((delegate* unmanaged<ulong, IntPtr*, int>)initialize)(version, &system);
        if (init != Ok || system == IntPtr.Zero)
        {
            return new GpuTuningReading(GpuReadStatus.Failed, [], "ADLXInitialize devolveu " + init);
        }

        try
        {
            return ReadSystem(system);
        }
        finally
        {
            _ = ((delegate* unmanaged<int>)terminate)();
        }
    }

    private static GpuTuningReading ReadSystem(IntPtr system)
    {
        if (CallOut(system, SystemGetGpus, out var list) != Ok || list == IntPtr.Zero)
        {
            return new GpuTuningReading(GpuReadStatus.NoDevice, []);
        }

        var services = IntPtr.Zero;
        var gpus = new List<GpuTuningInfo>();
        try
        {
            if (CallOut(system, SystemGetTuningServices, out services) != Ok)
            {
                services = IntPtr.Zero;
            }

            var count = (uint)Call(list, ListSize);
            for (uint i = 0; i < count; i++)
            {
                IntPtr gpu;
                var code = ((delegate* unmanaged<IntPtr, uint, IntPtr*, int>)Slot(list, ListAtGpu))(list, i, &gpu);
                if (code != Ok || gpu == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    gpus.Add(ReadGpu(gpu, services));
                }
                finally
                {
                    Free(gpu);
                }
            }
        }
        finally
        {
            Free(services);
            Free(list);
        }

        return gpus.Count == 0 ? new GpuTuningReading(GpuReadStatus.NoDevice, []) : new GpuTuningReading(GpuReadStatus.Read, gpus);
    }

    private static GpuTuningInfo ReadGpu(IntPtr gpu, IntPtr services)
    {
        var name = CallOut(gpu, GpuName, out var namePointer) == Ok ? Marshal.PtrToStringUTF8(namePointer) ?? string.Empty : string.Empty;
        if (services == IntPtr.Zero)
        {
            return new GpuTuningInfo(GpuVendor.Amd, name.Trim(), false, false, null, null, null, null, null, null, null, null, PowerUnit.Percent);
        }

        var atFactory = GpuFlag(services, ServicesIsAtFactory, gpu);
        var manualGraphics = GpuFlag(services, ServicesSupportsManualGraphics, gpu) == true;
        var manualPower = GpuFlag(services, ServicesSupportsManualPower, gpu) == true;
        TuningRange? clockRange = null;
        TuningRange? voltageRange = null;
        int? clock = null;
        int? voltage = null;
        if (manualGraphics)
        {
            ReadGraphics(services, gpu, ref clockRange, ref clock, ref voltageRange, ref voltage);
        }

        TuningRange? powerRange = null;
        int? power = null;
        int? powerDefault = null;
        if (manualPower)
        {
            ReadPower(services, gpu, ref powerRange, ref power, ref powerDefault);
        }

        return new GpuTuningInfo(GpuVendor.Amd, name.Trim(), manualGraphics, manualPower, atFactory, clockRange, clock, voltageRange, voltage, powerRange, power, powerDefault, PowerUnit.Percent);
    }

    private static void ReadGraphics(IntPtr services, IntPtr gpu, ref TuningRange? clockRange, ref int? clock, ref TuningRange? voltageRange, ref int? voltage)
    {
        var any = GpuObject(services, ServicesGetManualGraphics, gpu);
        if (any == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var second = Query(any, "IADLXManualGraphicsTuning2");
            if (second != IntPtr.Zero)
            {
                try
                {
                    clockRange = CallRange(second, Tuning2MaxFrequencyRange, out var range) == Ok ? range : null;
                    clock = CallInt(second, Tuning2MaxFrequency, out var value) == Ok ? value : null;
                    voltageRange = CallRange(second, Tuning2VoltageRange, out var volts) == Ok ? volts : null;
                    voltage = CallInt(second, Tuning2Voltage, out var volt) == Ok ? volt : null;
                }
                finally
                {
                    Free(second);
                }

                return;
            }

            var first = Query(any, "IADLXManualGraphicsTuning1");
            if (first == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var raw = stackalloc int[6];
                var code = ((delegate* unmanaged<IntPtr, int*, int*, int>)Slot(first, Tuning1Ranges))(first, raw, raw + 3);
                if (code == Ok)
                {
                    clockRange = new TuningRange(raw[0], raw[1], raw[2]);
                    voltageRange = new TuningRange(raw[3], raw[4], raw[5]);
                }
            }
            finally
            {
                Free(first);
            }
        }
        finally
        {
            Free(any);
        }
    }

    private static void ReadPower(IntPtr services, IntPtr gpu, ref TuningRange? range, ref int? current, ref int? standard)
    {
        var any = GpuObject(services, ServicesGetManualPower, gpu);
        if (any == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var power = Query(any, "IADLXManualPowerTuning");
            if (power == IntPtr.Zero)
            {
                return;
            }

            try
            {
                range = CallRange(power, PowerRange, out var limits) == Ok ? limits : null;
                current = CallInt(power, PowerCurrent, out var value) == Ok ? value : null;
            }
            finally
            {
                Free(power);
            }

            var withDefault = Query(any, "IADLXManualPowerTuning1");
            if (withDefault != IntPtr.Zero)
            {
                try
                {
                    standard = CallInt(withDefault, Power1Default, out var value) == Ok ? value : null;
                }
                finally
                {
                    Free(withDefault);
                }
            }
        }
        finally
        {
            Free(any);
        }
    }
}
