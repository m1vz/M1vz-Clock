using System.ComponentModel;
using System.Runtime.InteropServices;
using M1vClock.Core.Results;
using Microsoft.Extensions.Logging;

namespace M1vClock.Optimize.Memory;

public sealed partial class NtMemoryListSource(ILogger<NtMemoryListSource> logger) : IMemoryListSource
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const int PrioritySlots = 8;
    private const int ProcessPrivilegeFields = 22;
    private const string ProfileSingleProcessPrivilege = "SeProfileSingleProcessPrivilege";
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint PrivilegeEnabled = 0x2;
    private const int StatusPrivilegeNotHeld = unchecked((int)0xC0000061);
    private const int StatusAccessDenied = unchecked((int)0xC0000022);

    public ReadResult<MemoryListSnapshot> Read()
    {
        var size = ProcessPrivilegeFields * IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var status = NtQuerySystemInformation(SystemMemoryListInformation, buffer, size, out _);
            if (status != 0)
            {
                MemoryLog.ReadFailed(logger, status);
                return ReadResult.Failure<MemoryListSnapshot>($"O Windows respondeu com o código {status:X8}.");
            }

            long At(int index) => Marshal.ReadIntPtr(buffer, index * IntPtr.Size).ToInt64();
            var standby = new long[PrioritySlots];
            for (var i = 0; i < PrioritySlots; i++)
            {
                standby[i] = At(5 + i);
            }

            var info = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref info))
            {
                return ReadResult.Failure<MemoryListSnapshot>(new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            }

            var pageSize = Environment.SystemPageSize;
            return ReadResult.Success(new MemoryListSnapshot(pageSize, (long)(info.TotalPhys / (ulong)pageSize), At(0), At(1), At(2), At(3), standby));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public PurgeAttempt PurgeStandby()
    {
        if (!EnablePrivilege())
        {
            return new PurgeAttempt(PurgeStatus.NeedsAdministrator, null);
        }

        var command = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(command, MemoryPurgeStandbyList);
            var status = NtSetSystemInformation(SystemMemoryListInformation, command, sizeof(int));
            if (status == 0)
            {
                MemoryLog.Purged(logger);
                return new PurgeAttempt(PurgeStatus.Done, null);
            }

            MemoryLog.PurgeFailed(logger, status);
            if (status is StatusPrivilegeNotHeld or StatusAccessDenied)
            {
                return new PurgeAttempt(PurgeStatus.NeedsAdministrator, null);
            }

            return new PurgeAttempt(PurgeStatus.Failed, $"O Windows respondeu com o código {status:X8}.");
        }
        finally
        {
            Marshal.FreeHGlobal(command);
        }
    }

    private static bool EnablePrivilege()
    {
        if (!LookupPrivilegeValue(null, ProfileSingleProcessPrivilege, out var luid) || !OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
        {
            return false;
        }

        try
        {
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = PrivilegeEnabled };
            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastPInvokeError() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(int informationClass, IntPtr information, int length, out int returnLength);

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetSystemInformation(int informationClass, IntPtr information, int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges newState, int bufferLength, IntPtr previous, IntPtr returnLength);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
