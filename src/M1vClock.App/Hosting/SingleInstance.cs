using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace M1vClock.App.Hosting;

public sealed class SingleInstance : IDisposable
{
    private const string ActivateMessage = "activate";
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly string _userSid;
    private CancellationTokenSource? _listening;

    private SingleInstance(Mutex mutex, bool isPrimary, string pipeName, string userSid)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
        _pipeName = pipeName;
        _userSid = userSid;
    }

    public bool IsPrimary { get; }

    public static SingleInstance Acquire(string userSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userSid)))[..16];
        var mutex = new Mutex(initiallyOwned: true, $@"Local\m1vClock.{suffix}", out var created);
        return new SingleInstance(mutex, created, $"m1vClock.activate.{suffix}", userSid);
    }

    public void ActivatePrimary()
    {
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
        client.Connect(1500);
        var bytes = Encoding.UTF8.GetBytes(ActivateMessage);
        client.Write(bytes);
    }

    public void Listen(Action onActivate, Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(onActivate);
        ArgumentNullException.ThrowIfNull(onError);
        _listening = new CancellationTokenSource();
        var token = _listening.Token;
        _ = Task.Run(
            async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var server = NamedPipeServerStreamAcl.Create(
                            _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, PipeSecurity());
                        await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                        var buffer = new byte[32];
                        var read = await server.ReadAsync(buffer, token).ConfigureAwait(false);
                        if (Encoding.UTF8.GetString(buffer, 0, read) == ActivateMessage)
                        {
                            onActivate();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (IOException ex)
                    {
                        onError(ex);
                    }
                }
            },
            token);
    }

    public void Dispose()
    {
        _listening?.Cancel();
        _listening?.Dispose();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    private PipeSecurity PipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(_userSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
