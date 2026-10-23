using System.ComponentModel;
using System.Runtime.InteropServices;
using M1vClock.Core.Security;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Frames;

public sealed class EtwFrameEventSource(ElevationInfo elevation, ILogger<EtwFrameEventSource> logger) : IFrameEventSource
{
    public const string SessionName = "m1vClock-Frames";

    private const int DxgiPresentStart = 42;
    private const int D3D9PresentStart = 1;

    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");

    private readonly Lock _gate = new();
    private TraceEventSession? _session;
    private Thread? _thread;

    public event Action<FramePresent>? Presented;

    public FrameSourceResult Start()
    {
        if (!elevation.IsElevated)
        {
            return FrameSourceResult.NeedsAdministrator;
        }

        lock (_gate)
        {
            if (_session is not null)
            {
                return FrameSourceResult.Started;
            }

            try
            {
                var session = new TraceEventSession(SessionName, TraceEventSessionOptions.Create) { StopOnDispose = true };
                try
                {
                    session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, ulong.MaxValue, null);
                    session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, ulong.MaxValue, null);
                    session.Source.AllEvents += OnEvent;
                }
                catch
                {
                    session.Dispose();
                    throw;
                }

                _session = session;
                _thread = new Thread(Pump) { IsBackground = true, Name = "m1vClock ETW frames" };
                _thread.Start();
                return FrameSourceResult.Started;
            }
            catch (UnauthorizedAccessException ex)
            {
                FramesLog.EtwDenied(logger, ex);
                return FrameSourceResult.NeedsAdministrator;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or COMException or Win32Exception or ArgumentException or NotSupportedException)
            {
                FramesLog.EtwFailed(logger, ex);
                return FrameSourceResult.Failed;
            }
        }
    }

    public void Shutdown()
    {
        TraceEventSession? session;
        Thread? thread;
        lock (_gate)
        {
            session = _session;
            thread = _thread;
            _session = null;
            _thread = null;
        }

        if (session is null)
        {
            return;
        }

        try
        {
            session.Source.StopProcessing();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or Win32Exception or ObjectDisposedException)
        {
            FramesLog.EtwFailed(logger, ex);
        }

        session.Dispose();
        thread?.Join(TimeSpan.FromSeconds(3));
    }

    public void Dispose() => Shutdown();

    private void Pump()
    {
        TraceEventSession? session;
        lock (_gate)
        {
            session = _session;
        }

        try
        {
            session?.Source.Process();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or Win32Exception or ObjectDisposedException or IOException)
        {
            FramesLog.EtwFailed(logger, ex);
        }
    }

    private void OnEvent(TraceEvent data)
    {
        var id = (int)data.ID;
        var present = (data.ProviderGuid == DxgiProvider && id == DxgiPresentStart) || (data.ProviderGuid == D3D9Provider && id == D3D9PresentStart);
        if (present)
        {
            Presented?.Invoke(new FramePresent(data.ProcessID, data.TimeStampRelativeMSec));
        }
    }
}
