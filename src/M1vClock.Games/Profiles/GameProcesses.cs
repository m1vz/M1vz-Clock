using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace M1vClock.Games.Profiles;

public sealed record RunningProcess(int Id, string Path, DateTime? StartedUtc);

public interface IGameProcesses
{
    IReadOnlyList<RunningProcess> List();

    bool IsRunning(int processId);

    bool SetPriority(int processId, GamePriority priority);

    bool Close(int processId);
}

public sealed partial class WindowsGameProcesses(ILogger<WindowsGameProcesses> logger) : IGameProcesses
{
    private const uint QueryLimitedInformation = 0x1000;

    public IReadOnlyList<RunningProcess> List()
    {
        var found = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id <= 4 || ImagePath(process.Id) is not { } path)
                {
                    continue;
                }

                found.Add(new RunningProcess(process.Id, path, StartTime(process)));
            }
        }

        return found;
    }

    public bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            ProfileLog.ProcessGone(logger, ex, processId);
            return false;
        }
    }

    public bool SetPriority(int processId, GamePriority priority)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.PriorityClass = priority switch
            {
                GamePriority.High => ProcessPriorityClass.High,
                GamePriority.AboveNormal => ProcessPriorityClass.AboveNormal,
                _ => ProcessPriorityClass.Normal,
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            ProfileLog.PriorityDenied(logger, ex, processId);
            return false;
        }
    }

    public bool Close(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }

            if (process.CloseMainWindow() && process.WaitForExit(TimeSpan.FromSeconds(3)))
            {
                return true;
            }

            process.Kill();
            return process.WaitForExit(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            ProfileLog.HelperCloseFailed(logger, ex, processId);
            return false;
        }
    }

    private DateTime? StartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            ProfileLog.StartTimeUnknown(logger, ex, process.Id);
            return null;
        }
    }

    private static unsafe string? ImagePath(int processId)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, (uint)processId);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024u;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(nint process, uint flags, char* buffer, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}

internal static partial class ProfileLog
{
    [LoggerMessage(EventId = 7101, Level = LogLevel.Debug, Message = "Processo {ProcessId} já não existe.")]
    public static partial void ProcessGone(ILogger logger, Exception exception, int processId);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning, Message = "Prioridade do processo {ProcessId} não pôde ser alterada.")]
    public static partial void PriorityDenied(ILogger logger, Exception exception, int processId);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Warning, Message = "Programa auxiliar {ProcessId} não pôde ser fechado.")]
    public static partial void HelperCloseFailed(ILogger logger, Exception exception, int processId);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Debug, Message = "Hora de início do processo {ProcessId} indisponível.")]
    public static partial void StartTimeUnknown(ILogger logger, Exception exception, int processId);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Warning, Message = "Programa auxiliar {Helper} não abriu.")]
    public static partial void HelperFailed(ILogger logger, Exception exception, string helper);

    [LoggerMessage(EventId = 7106, Level = LogLevel.Warning, Message = "Preferência do Windows para {Exe} não foi aplicada: {Error}.")]
    public static partial void PreferenceFailed(ILogger logger, string exe, string? error);

    [LoggerMessage(EventId = 7107, Level = LogLevel.Information, Message = "Jogo {Game} aberto pelo perfil; processo {ProcessId}.")]
    public static partial void GameDetected(ILogger logger, string game, int processId);

    [LoggerMessage(EventId = 7108, Level = LogLevel.Information, Message = "Sessão de {Game} terminou como {Stage}.")]
    public static partial void SessionEnded(ILogger logger, string game, GameSessionStage stage);

    [LoggerMessage(EventId = 7109, Level = LogLevel.Warning, Message = "Não foi possível abrir {Game}.")]
    public static partial void LaunchFailed(ILogger logger, Exception exception, string game);

    [LoggerMessage(EventId = 7110, Level = LogLevel.Warning, Message = "Não foi possível gravar as opções de inicialização da Steam em {File}.")]
    public static partial void SteamWriteFailed(ILogger logger, Exception exception, string file);

    [LoggerMessage(EventId = 7111, Level = LogLevel.Information, Message = "Opções de inicialização da Steam gravadas para o app {AppId}; cópia em {Backup}.")]
    public static partial void SteamWritten(ILogger logger, string appId, string backup);
}
