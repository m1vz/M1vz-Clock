using System.ComponentModel;
using System.Runtime.InteropServices;

namespace M1vClock.Core.Native;

public static partial class NativeLibraryPolicy
{
    private const uint LoadLibrarySearchApplicationDir = 0x00000200;
    private const uint LoadLibrarySearchSystem32 = 0x00000800;

    public static void HardenProcess()
    {
        if (!SetDefaultDllDirectories(LoadLibrarySearchSystem32 | LoadLibrarySearchApplicationDir))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (!SetDllDirectoryW(string.Empty))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public static IntPtr LoadFromSystem32(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            throw new ArgumentException("Informe só o nome da DLL.", nameof(fileName));
        }

        return NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, fileName));
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDefaultDllDirectories(uint directoryFlags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectoryW(string pathName);
}
