using Avalonia;
using Terraria_Wiki.Windows.Hosting;
using Velopack;

namespace Terraria_Wiki.Windows;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WindowsStartupClock.MarkManagedEntry();
        WindowsStartupInvestigation.Configure(args);
        try
        {
            if (!args.Contains("--button-review", StringComparer.Ordinal)
                && !args.Contains("--hidden-webview-poc-log", StringComparer.Ordinal))
            {
                using var timing = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.Velopack);
                VelopackApp.Build().Run();
            }
            if (!args.Contains("--button-review", StringComparer.Ordinal)
                && !args.Contains("--smoke-log", StringComparer.Ordinal)
                && !args.Contains("--hidden-webview-poc-log", StringComparer.Ordinal))
            {
                using var timing = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.SingleInstanceActivation);
                if (SingleInstanceActivation.TryNotifyExisting()) return;
            }
            AppBuilder builder;
            using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AvaloniaBuilder))
                builder = BuildAvaloniaApp();
            WindowsStartupInvestigation.Checkpoint(StartupInvestigationPhase.FrameworkInitializationStarted);
            builder.StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            WindowsStartupInvestigation.Complete();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
