using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Frames;

public sealed class WindowsProcessNames(ILogger<WindowsProcessNames> logger) : IProcessNames
{
    public string NameOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            FramesLog.ProcessNameUnknown(logger, ex, processId);
            return $"PID {processId}";
        }
    }
}

public sealed partial class WindowsForegroundWindow : IForegroundWindow
{
    public int? ProcessId()
    {
        var window = GetForegroundWindow();
        if (window == 0)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(window, out var id);
        return id == 0 ? null : (int)id;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
