using System.Runtime.InteropServices;

namespace M1vClock.App.Overlay;

internal static partial class OverlayNative
{
    public const int WmHotkey = 0x0312;
    public const int NoRepeat = 0x4000;
    public const int ErrorHotkeyAlreadyRegistered = 1409;

    private const int ExtendedStyle = -20;
    private const long Transparent = 0x20;
    private const long Layered = 0x80000;
    private const long ToolWindow = 0x80;
    private const long NoActivate = 0x8000000;
    private const uint NoSize = 0x1;
    private const uint NoActivateFlag = 0x10;
    private const uint NoOwnerZOrder = 0x200;
    private const uint DefaultToNearest = 2;
    private const uint DefaultToPrimary = 1;
    private static readonly IntPtr TopMost = new(-1);

    public static void MakeClickThrough(IntPtr window)
    {
        var style = GetWindowLongPtr(window, ExtendedStyle).ToInt64();
        _ = SetWindowLongPtr(window, ExtendedStyle, new IntPtr(style | Transparent | Layered | ToolWindow | NoActivate));
    }

    public static IntPtr MonitorOf(IntPtr window) =>
        window == IntPtr.Zero ? MonitorFromPoint(default, DefaultToPrimary) : MonitorFromWindow(window, DefaultToNearest);

    public static bool TryMonitorBounds(IntPtr monitor, out NativeRect bounds)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var ok = GetMonitorInfo(monitor, ref info);
        bounds = info.Monitor;
        return ok;
    }

    public static bool TryWindowBounds(IntPtr window, out NativeRect bounds) => GetWindowRect(window, out bounds);

    public static void PlaceOnTop(IntPtr window, int x, int y) =>
        SetWindowPos(window, TopMost, x, y, 0, 0, NoSize | NoActivateFlag | NoOwnerZOrder);

    public static (IntPtr Window, int ProcessId) Foreground()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return (IntPtr.Zero, 0);
        }

        _ = GetWindowThreadProcessId(window, out var id);
        return (window, (int)id);
    }

    public static bool Register(IntPtr window, int id, int modifiers, int key, out int error)
    {
        var ok = RegisterHotKey(window, id, (uint)(modifiers | NoRepeat), (uint)key);
        error = ok ? 0 : Marshal.GetLastPInvokeError();
        return ok;
    }

    public static void Unregister(IntPtr window, int id) => _ = UnregisterHotKey(window, id);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial IntPtr GetWindowLongPtr(IntPtr window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr window, out NativeRect rect);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);
}
