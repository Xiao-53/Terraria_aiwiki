using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Reader;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows;

// Explicit command-line diagnostic only. It never participates in normal startup.
internal static class HiddenWebViewPoc
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    public static async Task RunAsync(
        Window owner,
        WindowsClientRuntime runtime,
        string logPath,
        string? mode,
        CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var logGate = new object();
        void Log(string stage, object? detail = null)
        {
            var line = JsonSerializer.Serialize(new
            {
                elapsedMs = clock.Elapsed.TotalMilliseconds,
                stage,
                detail,
                processPrivateBytes = Process.GetCurrentProcess().PrivateMemorySize64,
            });
            lock (logGate)
                File.AppendAllText(logPath, line + Environment.NewLine);
        }

        Window? widget = null;
        ViewerWebViewSession? session = null;
        try
        {
            Log("main-ready-poc-scheduled", new { mode = mode ?? "normal", owner.IsActive });
            await Task.Delay(mode == "early" ? 250 : 1500, cancellationToken);
            var reader = new ReaderView();
            var webView = reader.WebView;
            webView.EnvironmentRequested += (_, args) =>
            {
                if (args is WindowsWebView2EnvironmentRequestedEventArgs webView2)
                    webView2.UserDataFolder = Path.Combine(Path.GetDirectoryName(logPath)!, "webview2-poc");
            };
            widget = new Window { Title = "Hidden WebView PoC", Width = 520, Height = 600, Content = reader };
            var bridge = new ViewerBridge(runtime.Application, new WindowsExternalUriLauncher());
            session = new ViewerWebViewSession(webView, bridge);
            var sessionId = RuntimeHelpers.GetHashCode(session);
            var webViewId = RuntimeHelpers.GetHashCode(webView);
            webView.AdapterCreated += (_, _) => Log("adapter-created");
            webView.AdapterDestroyed += (_, _) => Log("adapter-destroyed");
            session.ReadyChanged += (_, ready) => Log("viewer-ready-changed", new { ready });
            session.PageCommitted += (_, page) => Log("page-committed", new { page.CanonicalTitle });
            webView.NavigationCompleted += (_, _) => Log("navigation-completed");
            Log("managed-constructed", Snapshot(widget, webView, session, sessionId, webViewId));

            if (runtime.Viewer.Status is not { State: ViewerConsumerState.Ready, BaseUri: not null } server)
            {
                Log("viewer-server-unavailable");
                return;
            }
            var wiki = await runtime.Application.ActiveWiki.GetActiveAsync(cancellationToken);
            session.Navigate(server.BaseUri, wiki.Id, wiki.Title);
            Log("hidden-navigation-requested", Snapshot(widget, webView, session, sessionId, webViewId));
            if (mode == "cancel")
            {
                await Task.Delay(250, cancellationToken);
                Log("cancel-requested", Snapshot(widget, webView, session, sessionId, webViewId));
                return;
            }
            if (mode != "early")
                await Task.Delay(4000, cancellationToken);
            Log("hidden-observation-ended", Snapshot(widget, webView, session, sessionId, webViewId));
            Log("main-focus-before-show", new { owner.IsActive, windowHandle = owner.TryGetPlatformHandle()?.Handle.ToInt64() });

            widget.Show(owner); // First display occurs only after the hidden observation.
            Log("first-show", Snapshot(widget, webView, session, sessionId, webViewId));
            Log("main-focus-after-show", new { owner.IsActive, windowHandle = owner.TryGetPlatformHandle()?.Handle.ToInt64() });
            await Task.Delay(4000, cancellationToken);
            Log("after-first-show", Snapshot(widget, webView, session, sessionId, webViewId));
            if (session.IsReady && wiki.IsPageDownloaded)
            {
                var bodyStarted = clock.Elapsed.TotalMilliseconds;
                await session.OpenPageAsync(wiki.DefaultPageTitle, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                Log("first-body-command-completed", new { elapsedFromRequestMs = clock.Elapsed.TotalMilliseconds - bodyStarted, session.CurrentPage });
            }
            else
            {
                Log("first-body-skipped", new { wiki.IsPageDownloaded, session.IsReady });
            }
        }
        catch (OperationCanceledException)
        {
            Log("cancelled");
        }
        catch (Exception exception)
        {
            Log("failed", new { type = exception.GetType().FullName, exception.Message });
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
            widget?.Close();
            Log("disposed");
            owner.Close();
        }
    }

    private static object Snapshot(
        Window window,
        NativeWebView webView,
        ViewerWebViewSession session,
        int sessionId,
        int webViewId)
    {
        var fields = new List<object>();
        for (var type = webView.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (!field.Name.Contains("adapter", StringComparison.OrdinalIgnoreCase)
                    && !field.Name.Contains("platform", StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = field.GetValue(webView);
                fields.Add(new { field = $"{type.Name}.{field.Name}", type = value?.GetType().FullName, id = value is null ? (int?)null : RuntimeHelpers.GetHashCode(value) });
            }
        }
        return new
        {
            windowHandle = window.TryGetPlatformHandle()?.Handle.ToInt64(),
            window.IsVisible,
            window.IsActive,
            window.ShowInTaskbar,
            foregroundWindowHandle = GetForegroundWindow().ToInt64(),
            adapterFields = fields,
            sessionId,
            webViewId,
            session.IsReady,
            session.ReceivedMessageCount,
            session.ValidMessageCount,
            session.CurrentPage,
        };
    }
}
