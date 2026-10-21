using System.IO;
using System.Reflection;
using System.Windows;
using M1vClock.App.Hosting;
using M1vClock.Core.Native;
using M1vClock.Core.Paths;
using M1vClock.Core.Security;
using Serilog;

namespace M1vClock.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        NativeLibraryPolicy.HardenProcess();
        var options = StartupOptions.Parse(args);
        var elevation = ElevationInfo.Current();
        var paths = AppPaths.ForProcess(elevation);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.EnsureDirectory(paths.Logs), "m1vclock-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            using var instance = SingleInstance.Acquire(elevation.UserSid);
            if (!instance.IsPrimary)
            {
                ActivateRunningInstance(instance);
                return 0;
            }

            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
            using var host = AppHost.Build(paths, elevation, options, version);
            var app = new App(host, instance, version);
            app.InitializeComponent();
            return app.Run();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Fatal(ex, "Falha ao iniciar o m1vClock.");
            MessageBox.Show(
                $"O m1vClock não conseguiu abrir: {ex.Message}\n\nOs detalhes ficaram no log em {paths.Logs}.",
                "m1vClock",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ActivateRunningInstance(SingleInstance instance)
    {
        try
        {
            instance.ActivatePrimary();
            Log.Information("Outra janela do m1vClock já estava aberta; ela foi trazida para a frente.");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "O m1vClock já está aberto, mas não respondeu ao pedido para vir para a frente.");
        }
    }
}
