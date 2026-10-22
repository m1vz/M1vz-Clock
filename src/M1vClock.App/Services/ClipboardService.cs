using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace M1vClock.App.Services;

public interface IClipboardService
{
    bool TrySetText(string text);
}

public sealed class WpfClipboardService(ILogger<WpfClipboardService> logger) : IClipboardService
{
    public bool TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (COMException ex)
        {
            AppLog.ClipboardBusy(logger, ex);
            return false;
        }
    }
}
