using System.Diagnostics;
using System.Runtime.InteropServices;
using M1vClock.Core.Security;
using Microsoft.Extensions.Logging;

namespace M1vClock.Core.Shell;

public interface IShellLauncher
{
    void OpenUrl(Uri url);

    void OpenFolder(string path);

    void Start(string fileName, string? arguments, string? workingDirectory);
}

public sealed class ShellLauncher(ElevationInfo elevation, ILogger<ShellLauncher> logger) : IShellLauncher
{
    private const int ShowNormal = 1;

    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException("Só endereços http e https podem ser abertos.", nameof(url));
        }

        Launch(url.AbsoluteUri, null, null);
    }

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(path);
        }

        Launch(Path.GetFullPath(path), null, null);
    }

    public void Start(string fileName, string? arguments, string? workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Launch(fileName, arguments, workingDirectory);
    }

    private void Launch(string target, string? arguments, string? workingDirectory)
    {
        if (!elevation.IsElevated)
        {
            using var process = Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = workingDirectory ?? string.Empty,
            });
            CoreLog.OpenedByShell(logger, target);
            return;
        }

        DesktopShell.ShellExecute(target, arguments ?? string.Empty, workingDirectory ?? string.Empty, "open", ShowNormal);
        CoreLog.OpenedDeElevated(logger, target);
    }

    private static class DesktopShell
    {
        private const int ShellWindowClassDesktop = 8;
        private const int FindWindowNeedDispatch = 1;
        private const uint ShellViewBackground = 0;

        private static readonly Guid ShellWindowsClassId = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid TopLevelBrowserService = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid ShellBrowserInterface = new("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid DispatchInterface = new("00020400-0000-0000-C000-000000000046");

        public static void ShellExecute(string file, string arguments, string directory, string operation, int show)
        {
            var shellWindowsType = Type.GetTypeFromCLSID(ShellWindowsClassId, throwOnError: true)!;
            var shellWindows = (IShellWindows)Activator.CreateInstance(shellWindowsType)!;
            object? desktop = null;
            object? folderView = null;
            try
            {
                object location = 0;
                object root = null!;
                desktop = shellWindows.FindWindowSW(ref location, ref root, ShellWindowClassDesktop, out _, FindWindowNeedDispatch)
                    ?? throw new InvalidOperationException("O Explorer não está em execução; não há como abrir sem administrador.");

                var service = TopLevelBrowserService;
                var browserInterface = ShellBrowserInterface;
                var browser = (IShellBrowser)((IServiceProvider)desktop).QueryService(ref service, ref browserInterface);
                var view = browser.QueryActiveShellView();
                var dispatch = DispatchInterface;
                folderView = view.GetItemObject(ShellViewBackground, ref dispatch);
                dynamic dualView = folderView;
                dynamic application = dualView.Application;
                application.ShellExecute(file, arguments, directory, operation, show);
            }
            finally
            {
                Release(folderView);
                Release(desktop);
                Release(shellWindows);
            }
        }

        private static void Release(object? com)
        {
            if (com is not null && Marshal.IsComObject(com))
            {
                Marshal.ReleaseComObject(com);
            }
        }
    }

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object Item([MarshalAs(UnmanagedType.Struct)] object index);

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object NewEnum();

        int Register([MarshalAs(UnmanagedType.IDispatch)] object dispatch, int hwnd, int classId);

        int RegisterPending(int threadId, [MarshalAs(UnmanagedType.Struct)] ref object location, [MarshalAs(UnmanagedType.Struct)] ref object root, int classId);

        void Revoke(int cookie);

        void OnNavigate(int cookie, [MarshalAs(UnmanagedType.Struct)] ref object location);

        void OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? FindWindowSW(
            [MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object root,
            int classId,
            out int hwnd,
            int options);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid service, ref Guid riid);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();

        void ContextSensitiveHelp();

        void InsertMenusSB();

        void SetMenuSB();

        void RemoveMenusSB();

        void SetStatusTextSB();

        void EnableModelessSB();

        void TranslateAcceleratorSB();

        void BrowseObject();

        void GetViewStateStream();

        void GetControlWindow();

        void SendControlMsg();

        IShellView QueryActiveShellView();
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();

        void ContextSensitiveHelp();

        void TranslateAccelerator();

        void EnableModeless();

        void UIActivate();

        void Refresh();

        void CreateViewWindow();

        void DestroyViewWindow();

        void GetCurrentInfo();

        void AddPropertySheetPages();

        void SaveViewState();

        void SelectItem();

        [return: MarshalAs(UnmanagedType.Interface)]
        object GetItemObject(uint item, ref Guid riid);
    }
}
