using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Terraria_Wiki.Windows.Hosting;

namespace Terraria_Wiki.Windows;

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        using var timing = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.ApplicationXaml);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        WindowsStartupInvestigation.Checkpoint(StartupInvestigationPhase.FrameworkInitialized);
        using var timing = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.MainWindowConstructor);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = (desktop.Args ?? []).Contains("--button-review", StringComparer.Ordinal)
                ? new Presentation.Review.ButtonReviewWindow()
                : new MainWindow(desktop.Args ?? []);
            if (desktop.MainWindow is MainWindow)
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
