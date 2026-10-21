using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace M1vClock.Core.Processes;

public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments)
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    public string? WorkingDirectory { get; init; }

    public Encoding? OutputEncoding { get; init; }
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration, bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public static class SystemTools
{
    public static string System32(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        if (executable.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            throw new ArgumentException("Informe só o nome do executável.", nameof(executable));
        }

        var path = Path.Combine(Environment.SystemDirectory, executable);
        return File.Exists(path) ? path : throw new FileNotFoundException("Ferramenta do Windows não encontrada.", path);
    }
}

public sealed partial class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private const int MaxLoggedCharacters = 16_000;

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.FileName))
        {
            throw new ArgumentException("O executável precisa de caminho absoluto.", nameof(request));
        }

        var encoding = request.OutputEncoding ?? ConsoleEncoding();
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
            WorkingDirectory = request.WorkingDirectory ?? Environment.SystemDirectory,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var commandLine = Describe(request);
        CoreLog.ProcessStarting(logger, commandLine);
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Não foi possível iniciar {request.FileName}.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (!timedOut)
            {
                CoreLog.ProcessCancelled(logger, commandLine);
                throw;
            }

            CoreLog.ProcessTimedOut(logger, commandLine, request.Timeout.TotalSeconds);
        }

        var result = new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            await output.ConfigureAwait(false),
            await error.ConfigureAwait(false),
            stopwatch.Elapsed,
            timedOut);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var seconds = Math.Round(result.Duration.TotalSeconds, 1);
            var loggedOutput = Truncate(result.StandardOutput);
            var loggedError = Truncate(result.StandardError);
            CoreLog.ProcessFinished(logger, commandLine, result.ExitCode, seconds, loggedOutput, loggedError);
        }

        return result;
    }

    public static string Describe(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder(Quote(request.FileName));
        foreach (var argument in request.Arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string value) =>
        value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0 ? value : "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Truncate(string text) =>
        text.Length <= MaxLoggedCharacters ? text : string.Concat(text.AsSpan(0, MaxLoggedCharacters), " …");

    private static Encoding ConsoleEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var codePage = (int)GetOEMCP();
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (ArgumentException)
        {
            return Encoding.GetEncoding(string.Create(CultureInfo.InvariantCulture, $"ibm{codePage}"));
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();
}
