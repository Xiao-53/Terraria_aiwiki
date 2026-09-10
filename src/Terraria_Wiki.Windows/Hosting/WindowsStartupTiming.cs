using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Terraria_Wiki.Windows.Hosting;

internal enum WindowsStartupStage
{
    ManagedEntry,
    WindowConstructed,
    WindowOpened,
    WebView2Probe,
    DataRootAndLocks,
    DataLayoutPreflight,
    ManagerInitializationAndRecovery,
    Settings,
    CompatibilityPreparation,
    ContentInitialization,
    StatisticsSnapshot,
    RuntimePreferences,
    RuntimeReady,
    ViewerServer,
    ViewerNavigationStarted,
    ViewerNavigationCompleted,
    ViewerDomReady,
}

internal readonly record struct WindowsStartupStageMeasurement(
    WindowsStartupStage Stage,
    long DurationMilliseconds);

internal sealed record WindowsStartupStageTiming(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("elapsed_ms")] long ElapsedMilliseconds,
    [property: JsonPropertyName("duration_ms")] long DurationMilliseconds);

internal static class WindowsStartupClock
{
    private static long _managedEntryMilliseconds = -1;

    internal static long ManagedEntryMilliseconds => Math.Max(0, Volatile.Read(ref _managedEntryMilliseconds));

    internal static void MarkManagedEntry() =>
        Interlocked.CompareExchange(ref _managedEntryMilliseconds, ElapsedFromProcessStart(), -1);

    internal static long ElapsedFromProcessStart()
    {
        try
        {
            return Math.Max(
                0,
                (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);
        }
        catch
        {
            return 0;
        }
    }

    internal static string Name(WindowsStartupStage stage) => stage switch
    {
        WindowsStartupStage.ManagedEntry => "managed-entry",
        WindowsStartupStage.WindowConstructed => "window-constructed",
        WindowsStartupStage.WindowOpened => "window-opened",
        WindowsStartupStage.WebView2Probe => "webview2-probe",
        WindowsStartupStage.DataRootAndLocks => "data-root-and-locks",
        WindowsStartupStage.DataLayoutPreflight => "data-layout-preflight",
        WindowsStartupStage.ManagerInitializationAndRecovery => "manager-initialization-and-recovery",
        WindowsStartupStage.Settings => "settings",
        WindowsStartupStage.CompatibilityPreparation => "compatibility-preparation",
        WindowsStartupStage.ContentInitialization => "content-initialization",
        WindowsStartupStage.StatisticsSnapshot => "statistics-snapshot",
        WindowsStartupStage.RuntimePreferences => "runtime-preferences",
        WindowsStartupStage.RuntimeReady => "runtime-ready",
        WindowsStartupStage.ViewerServer => "viewer-server",
        WindowsStartupStage.ViewerNavigationStarted => "viewer-navigation-started",
        WindowsStartupStage.ViewerNavigationCompleted => "viewer-navigation-completed",
        WindowsStartupStage.ViewerDomReady => "viewer-dom-ready",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };
}
