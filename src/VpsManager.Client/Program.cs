using Avalonia;
using System;
using System.Threading.Tasks;
using VpsManager.Core.Services.Logging;

namespace VpsManager.Client;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logger = new AppLogger();
        logger.Info("Bridge workstation starting up...");

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                logger.Fatal("Unhandled AppDomain Exception encountered", ex);
                string dumpPath = CrashReporter.CreateCrashDump(ex, logger);
                Console.Error.WriteLine($"FATAL CRASH: Diagnostic report written to: {dumpPath}");
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            logger.Error("Unobserved Task Exception encountered", e.Exception);
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.Fatal("Fatal exception in Avalonia desktop lifetime", ex);
            string dumpPath = CrashReporter.CreateCrashDump(ex, logger);
            Console.Error.WriteLine($"FATAL CRASH: Diagnostic report written to: {dumpPath}");
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
