using System.Runtime.InteropServices;

namespace M1vClock.Benchmarks.Cpu;

public static partial class CpuTopology
{
    private const int RelationProcessorCore = 0;
    private const int ErrorInsufficientBuffer = 122;

    public static int LogicalProcessors => Environment.ProcessorCount;

    public static int PhysicalCores()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, 0, ref length);
        if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer || length == 0)
        {
            return LogicalProcessors;
        }

        var buffer = Marshal.AllocHGlobal((nint)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
            {
                return LogicalProcessors;
            }

            return Math.Clamp(CountCores(buffer, length), 1, LogicalProcessors);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static int CountCores(nint buffer, uint length)
    {
        var cores = 0;
        uint offset = 0;
        while (offset + 8 <= length)
        {
            var relationship = Marshal.ReadInt32(buffer, (int)offset);
            var size = (uint)Marshal.ReadInt32(buffer, (int)offset + 4);
            if (size == 0)
            {
                break;
            }

            if (relationship == RelationProcessorCore)
            {
                cores++;
            }

            offset += size;
        }

        return cores;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLogicalProcessorInformationEx(int relationshipType, nint buffer, ref uint returnedLength);
}
