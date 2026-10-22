using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace M1vClock.Benchmarks.Gpu;

internal sealed unsafe partial class RenderWindow : IDisposable
{
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WmClose = 0x0010;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysCommand = 0x0112;
    private const uint ScKeyMenu = 0xF100;
    private const int VkEscape = 0x1B;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int IdcArrow = 32512;
    private const uint PmRemove = 1;
    private const double ScreenFill = 0.85;

    private static readonly ConcurrentDictionary<nint, RenderWindow> Windows = new();

    private readonly nint _className;
    private readonly nint _instance;
    private volatile bool _closeRequested;
    private bool _disposed;

    private RenderWindow(nint handle, nint className, nint instance)
    {
        Handle = handle;
        _className = className;
        _instance = instance;
    }

    public nint Handle { get; }

    public bool CloseRequested => _closeRequested;

    public static RenderWindow Create(string title, int width, int height, bool fullscreen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        var instance = GetModuleHandleW(null);
        var className = Marshal.StringToHGlobalUni("m1vClock.RenderWindow." + Guid.NewGuid().ToString("N"));
        var screenWidth = GetSystemMetrics(SmCxScreen);
        var screenHeight = GetSystemMetrics(SmCyScreen);
        var registration = new WindowClass
        {
            Size = (uint)sizeof(WindowClass),
            WindowProcedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProcedure,
            Instance = instance,
            Cursor = LoadCursorW(0, IdcArrow),
            ClassName = className,
        };
        if (RegisterClassExW(ref registration) == 0)
        {
            Marshal.FreeHGlobal(className);
            throw new InvalidOperationException("Não foi possível registrar a janela de renderização.");
        }

        int x, y, w, h;
        uint style;
        if (fullscreen)
        {
            style = WsPopup | WsVisible;
            x = 0;
            y = 0;
            w = screenWidth;
            h = screenHeight;
        }
        else
        {
            var scale = Math.Min(1.0, Math.Min(screenWidth * ScreenFill / width, screenHeight * ScreenFill / height));
            style = WsOverlappedWindow | WsVisible;
            var rect = new Rect { Right = Math.Max(1, (int)(width * scale)), Bottom = Math.Max(1, (int)(height * scale)) };
            AdjustWindowRect(ref rect, style, false);
            w = rect.Right - rect.Left;
            h = rect.Bottom - rect.Top;
            x = Math.Max(0, (screenWidth - w) / 2);
            y = Math.Max(0, (screenHeight - h) / 2);
        }

        nint handle;
        fixed (char* titlePointer = title)
        {
            handle = CreateWindowExW(0, (char*)className, titlePointer, style, x, y, w, h, 0, 0, instance, 0);
        }

        if (handle == 0)
        {
            UnregisterClassW((char*)className, instance);
            Marshal.FreeHGlobal(className);
            throw new InvalidOperationException("Não foi possível criar a janela de renderização.");
        }

        var window = new RenderWindow(handle, className, instance);
        Windows[handle] = window;
        SetForegroundWindow(handle);
        return window;
    }

    public static void Pump()
    {
        while (PeekMessageW(out var message, 0, 0, 0, PmRemove))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    public (int Width, int Height) ClientSize()
    {
        GetClientRect(Handle, out var rect);
        return (rect.Right, rect.Bottom);
    }

    public void SetTitle(string title)
    {
        SetWindowTextW(Handle, title);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Windows.TryRemove(Handle, out _);
        DestroyWindow(Handle);
        UnregisterClassW((char*)_className, _instance);
        Marshal.FreeHGlobal(_className);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmSysCommand && ((uint)wParam & 0xFFF0) == ScKeyMenu)
        {
            return 0;
        }

        if (message == WmClose || (message == WmKeyDown && wParam == VkEscape))
        {
            if (Windows.TryGetValue(window, out var target))
            {
                target._closeRequested = true;
            }

            return 0;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WindowClass windowClass);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(char* className, nint instance);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Message message, nint window, uint minimum, uint maximum, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Message message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(ref Message message);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRect(ref Rect rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowTextW(nint window, string text);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
