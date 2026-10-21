using System.ComponentModel;
using System.Runtime.InteropServices;

namespace M1vClock.Core.Security;

public static partial class TokenPrivileges
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint PrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;

    public static IDisposable Enable(params string[] privileges)
    {
        ArgumentNullException.ThrowIfNull(privileges);
        var enabled = new List<string>();
        try
        {
            foreach (var privilege in privileges)
            {
                Set(privilege, enable: true);
                enabled.Add(privilege);
            }
        }
        catch
        {
            foreach (var privilege in enabled)
            {
                Set(privilege, enable: false);
            }

            throw;
        }

        return new Scope(enabled);
    }

    private static void Set(string privilege, bool enable)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            if (!LookupPrivilegeValueW(null, privilege, out var luid))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var state = new TokenPrivilege
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = enable ? PrivilegeEnabled : 0,
            };

            if (!AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (enable && Marshal.GetLastPInvokeError() == ErrorNotAllAssigned)
            {
                throw new Win32Exception(ErrorNotAllAssigned, $"O privilégio {privilege} não está disponível para este processo.");
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private sealed class Scope(List<string> privileges) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var privilege in privileges)
            {
                Set(privilege, enable: false);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivilege
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? systemName, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        IntPtr token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivilege newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);
}
