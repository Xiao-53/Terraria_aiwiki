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
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
