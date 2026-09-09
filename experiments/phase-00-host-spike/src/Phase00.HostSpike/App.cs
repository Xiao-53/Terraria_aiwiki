using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Phase00.HostSpike;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(desktop.Args ?? Array.Empty<string>());
            desktop.Exit += (_, _) => MainWindow.CleanupAtExit();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
