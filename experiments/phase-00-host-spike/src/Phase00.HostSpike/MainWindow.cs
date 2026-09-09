using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Phase00.Probes;
using SQLite;

namespace Phase00.HostSpike;

public sealed class MainWindow : Window
{
    private static MainWindow? _activeWindow;
    private readonly string[] _args;
    private readonly TextBlock _status;
    private readonly NativeWebView _webView;
    private readonly Button _smokeCancel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PlatformProbeChecklist _platform = new();
    private ViewerServer? _server;
    private SqliteWikiViewerBridge? _bridge;
    private string? _tempRoot;
    private string? _databasePath;
    private string? _smokeLogPath;
    private string? _probeLogPath;
    private string? _origin;
    private int _messageCount;
    private int _validMessageCount;
    private bool _smokeMode;
    private bool _smokeCompleted;
    private bool _pageMessageSeen;
    private bool _assetSeen;
    private bool _imageResourceServed;
    private bool _audioAssetSeen;
    private bool _audioResourceServed;
    private bool _internalLinkSeen;
    private bool _themeMessageSeen;
    private bool _viewerDarkThemeSeen;
    private bool _historyMessageSeen;
    private bool _linkResponseSeen;
    private bool _backResponseSeen;
    private bool _webMessageReceived;
    private int _invalidMessageCount;
    private int _validResponseCount;
    private int _errorMessageCount;
    private bool _commandsSent;
    private bool _backCommandSent;
    private bool _transportIdempotent;
    private bool _transportConflictObserved;
    private bool _transportStopped;
    private bool _transportRestarted;
    private bool _transportClosed;
    private bool _tempRootCleaned;
    private WebView2RuntimeEvidence _webView2 = WebView2RuntimeEvidence.Unknown;
    private DateTimeOffset _smokeStarted;
    private int _cleanupStarted;
    private bool _smokeSuccess;
    private string _smokeMessage = string.Empty;
    private SmokeError? _smokeError;

    public MainWindow(string[] args)
    {
        _activeWindow = this;
        _args = args;
        Title = "Phase 00 — Avalonia + NativeWebView Host Spike";
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 600;
        _platform.WindowOpened = true;
        _platform.WindowWidth = Width;
        _platform.WindowHeight = Height;

        _status = new TextBlock { Text = "Starting disposable host…", Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap };
        _webView = new NativeWebView { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        _webView.WebMessageReceived += (_, eventArgs) =>
        {
            _webMessageReceived = true;
            _ = HandleWebMessageAsync(eventArgs.Body);
        };
        _webView.NavigationCompleted += (_, _) => SetStatus("Viewer navigation completed.");

        _smokeCancel = new Button { Content = "Cancel smoke", IsVisible = false, Margin = new Thickness(3) };
        _smokeCancel.Click += (_, _) => FinishSmoke(false, "Smoke cancelled by user.");

        Content = BuildLayout();
        Opened += async (_, _) => await StartAsync();
        Closing += (_, _) => _lifetime.Cancel();
    }

    public static void CleanupAtExit() => _activeWindow?.Cleanup();

    private Control BuildLayout()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 8, 8, 0) };
        buttons.Children.Add(ActionButton("Select file", SelectFileAsync));
        buttons.Children.Add(ActionButton("Select folder", SelectFolderAsync));
        buttons.Children.Add(ActionButton("Clipboard", ClipboardAsync));
        buttons.Children.Add(ActionButton("Open external (manual)", OpenExternalAsync));
        buttons.Children.Add(ActionButton("Toggle theme", ToggleTheme));
        buttons.Children.Add(ActionButton("Cancel/close", () => Close()));
        buttons.Children.Add(_smokeCancel);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        grid.Children.Add(buttons);
        Grid.SetRow(_status, 1);
        grid.Children.Add(_status);
        Grid.SetRow(_webView, 2);
        grid.Children.Add(_webView);
        return grid;
    }

    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3) };
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex) { SetStatus($"Action failed: {ex.Message}"); }
        };
        return button;
    }

    private Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { SetStatus($"Action failed: {ex.Message}"); }
        };
        return button;
    }

    private async Task StartAsync()
    {
        try
        {
            _smokeStarted = DateTimeOffset.UtcNow;
            var parsed = ParseArgs(_args);
            _smokeMode = parsed.Smoke;
            _smokeLogPath = parsed.SmokeLogPath;
            _probeLogPath = parsed.ProbeLogPath;
            _smokeCancel.IsVisible = _smokeMode;
            ValidateLogPath(_smokeLogPath, "Smoke");
            ValidateLogPath(_probeLogPath, "Probe");

            _webView2 = ProbeWebView2Runtime();
            _platform.WebView2Available = _webView2.Available;
            _platform.WebView2Version = _webView2.Version;
            _platform.WebView2Error = _webView2.Error;
            SetStatus(_webView2.Available
                ? $"WebView2 Runtime {_webView2.Version} detected ({_webView2.Source})."
                : $"WebView2 Runtime unavailable: {_webView2.Error}");
            WriteProbeLog();
            if (!_webView2.Available)
            {
                if (_smokeMode) FinishSmoke(false, $"WebView2 Runtime unavailable: {_webView2.Error}");
                return;
            }

            _tempRoot = Path.Combine(Path.GetTempPath(), $"phase00-host-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempRoot);
            _databasePath = Path.Combine(_tempRoot, "fixture.db");
            await SqliteProbe.CreateFixtureAsync(_databasePath, _lifetime.Token);
            await AddAudioFixtureAsync(_databasePath, _lifetime.Token);
            _bridge = new SqliteWikiViewerBridge(_databasePath);

            var staticRoot = Path.Combine(AppContext.BaseDirectory, "ViewerAssets");
            if (!Directory.Exists(staticRoot))
                throw new DirectoryNotFoundException($"Viewer build assets are missing: {staticRoot}");
            _server = new ViewerServer(new ViewerServerOptions
            {
                StaticRoot = staticRoot,
                DataSource = new SqliteViewerDataSource(_databasePath)
            });
            var status = await _server.StartAsync(_lifetime.Token);
            var idempotent = await _server.StartAsync(_lifetime.Token);
            _transportIdempotent = idempotent.Running && idempotent.Port == status.Port;
            await using (var conflict = new ViewerServer(new ViewerServerOptions
            {
                StaticRoot = staticRoot,
                DataSource = new SqliteViewerDataSource(_databasePath),
                Port = status.Port
            }))
            {
                try { await conflict.StartAsync(_lifetime.Token); }
                catch (ViewerServerStartException) { _transportConflictObserved = true; }
            }
            _origin = status.Origin.TrimEnd('/');
            SetStatus($"Viewer server listening at {_origin}; static assets linked from the existing source. "
                + $"WebView2 {_webView2.Version}; transport idempotent={_transportIdempotent}, conflict={_transportConflictObserved}.");
            _webView.Source = new Uri(status.Origin);

            if (_smokeMode)
            {
                _smokeStarted = DateTimeOffset.UtcNow;
                _ = RunSmokeWatchdogAsync(_lifetime.Token);
            }
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(_webView2.Error))
            {
                _webView2 = _webView2 with { Error = $"NativeWebView initialization failed ({ex.GetType().Name}): {ex.Message}" };
                _platform.WebView2Error = _webView2.Error;
            }
            SetStatus($"Startup error: {ex.Message}");
            if (_smokeMode) FinishSmoke(false, ex.Message, ex);
        }
    }

    private async Task HandleWebMessageAsync(string? body)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                _invalidMessageCount++;
                return;
            }

            OuterWebMessage? message;
            try { message = JsonSerializer.Deserialize<OuterWebMessage>(body, JsonOptions); }
            catch (JsonException)
            {
                _invalidMessageCount++;
                return;
            }

            if (message is null || message.Source != "phase00-viewer" || string.IsNullOrWhiteSpace(message.Origin)
                || !string.Equals(message.Origin.TrimEnd('/'), _origin, StringComparison.Ordinal)
                || message.Envelope.ValueKind != JsonValueKind.Object)
            {
                _invalidMessageCount++;
                return;
            }

            var envelope = message.Envelope;
            var envelopeType = envelope.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (string.Equals(envelopeType, "res", StringComparison.Ordinal))
            {
                _validResponseCount++;
                if (envelope.TryGetProperty("error", out _)) _errorMessageCount++;
                var responseId = envelope.TryGetProperty("id", out var responseIdElement) ? responseIdElement.GetString() : null;
                if (responseId == "phase00-link") _linkResponseSeen = true;
                if (responseId == "phase00-back") _backResponseSeen = true;
                if (responseId == "phase00-theme")
                {
                    _themeMessageSeen = true;
                    var className = await _webView.InvokeScript("window.frames[0].document.documentElement.className");
                    _viewerDarkThemeSeen = className?.Contains("dark", StringComparison.OrdinalIgnoreCase) == true;
                    if (_viewerDarkThemeSeen) _bridge?.ChangeTheme("True");
                }
                if (_smokeMode && _linkResponseSeen && !_backCommandSent)
                {
                    _backCommandSent = true;
                    await SendIframeRequestAsync("phase00-back", "BackToPage", "{\"title\":\"Terraria Wiki\",\"position\":0}");
                }
                TryFinishSmoke();
                return;
            }

            if (!string.Equals(envelopeType, "req", StringComparison.Ordinal))
            {
                _invalidMessageCount++;
                return;
            }

            var method = envelope.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
            var id = envelope.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var data = envelope.TryGetProperty("data", out var dataElement) ? dataElement : default;
            if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(id))
            {
                _invalidMessageCount++;
                return;
            }
            _validMessageCount++;
            _messageCount++;

            switch (method)
            {
                case "PageRedirectAsync":
                {
                    var title = data.ValueKind == JsonValueKind.String ? data.GetString() ?? string.Empty : string.Empty;
                    var page = await _bridge!.PageRedirectAsync(title, _lifetime.Token);
                    if (page is not null)
                    {
                        _pageMessageSeen = true;
                        if (page.Content.Contains("/src/fixture.png", StringComparison.Ordinal)) _assetSeen = true;
                        if (page.Content.Contains("/src/fixture.ogg", StringComparison.Ordinal)) _audioAssetSeen = true;
                        if (page.Content.Contains("Alias", StringComparison.Ordinal)) _internalLinkSeen = true;
                    }
                    await SendResponseAsync(id!, page is null ? null : JsonSerializer.Serialize(page, JsonOptions));
                    break;
                }
                case "GetRedirectedTitleAndAnchorAsync":
                {
                    var input = data.ValueKind == JsonValueKind.String ? data.GetString() ?? string.Empty : string.Empty;
                    var result = await _bridge!.GetRedirectedTitleAndAnchorAsync(input, _lifetime.Token);
                    await SendResponseAsync(id!, JsonSerializer.Serialize(result, JsonOptions));
                    break;
                }
                case "SaveToTabHistory":
                {
                    await _bridge!.SaveToTabHistoryAsync(data.ValueKind == JsonValueKind.String ? data.GetString() ?? "{}" : "{}", _lifetime.Token);
                    _historyMessageSeen = true;
                    await SendResponseAsync(id!, null);
                    break;
                }
                case "ChangeTheme":
                    _bridge!.ChangeTheme(data.ValueKind == JsonValueKind.String ? data.GetString() ?? "False" : "False");
                    _themeMessageSeen = true;
                    await SendResponseAsync(id!, null);
                    break;
                default:
                    // Unknown/invalid methods are acknowledged as an error and
                    // are deliberately excluded from the success counters.
                    _invalidMessageCount++;
                    _errorMessageCount++;
                    await SendErrorAsync(id!, "unknown-method");
                    break;
            }

            if (_smokeMode && _pageMessageSeen && _assetSeen && _internalLinkSeen && !_commandsSent)
            {
                // Exercise the old C# -> iframe side explicitly after the
                // initial page has rendered: an internal redirect and theme.
                _commandsSent = true;
                await SendIframeRequestAsync("phase00-link", "GotoPage", "Alias");
                await SendIframeRequestAsync("phase00-theme", "ChangeTheme", "True");
                var className = await _webView.InvokeScript("window.frames[0].document.documentElement.className");
                _themeMessageSeen = className?.Contains("dark", StringComparison.OrdinalIgnoreCase) == true;
                if (_themeMessageSeen) _bridge!.ChangeTheme("True");
                // Send an intentionally invalid same-origin message.  The
                // host must observe and ignore it; it must never increase the
                // valid request count or make the smoke pass.
                await _webView.InvokeScript("window.chrome.webview.postMessage(JSON.stringify({source:'evil',origin:location.origin,envelope:{type:'req',id:'invalid',method:'PageRedirectAsync',data:'Guide'}}))");
            }

            TryFinishSmoke();
        }
        catch (Exception ex)
        {
            SetStatus($"Web message error: {ex.Message}");
            if (_smokeMode) FinishSmoke(false, ex.Message, ex);
        }
    }

    private void TryFinishSmoke()
    {
        _imageResourceServed = _imageResourceServed || _server?.GetRequestCount("/src/fixture.png") > 0;
        _audioResourceServed = _audioResourceServed || _server?.GetRequestCount("/src/fixture.ogg") > 0;
        if (!_smokeMode || _smokeCompleted || !_pageMessageSeen || !_assetSeen || !_imageResourceServed || !_audioAssetSeen
            || !_audioResourceServed || !_internalLinkSeen || !_themeMessageSeen || !_viewerDarkThemeSeen || !_historyMessageSeen || !_linkResponseSeen
            || !_backResponseSeen || _validMessageCount < 4 || _invalidMessageCount < 1 || _errorMessageCount > 0)
            return;

        if (_bridge is { History.Count: > 0 }) FinishSmoke(true, "NativeWebView Viewer smoke passed.");
    }

    private async Task SendResponseAsync(string id, string? data)
    {
        if (_origin is null) return;
        var response = new { type = "res", id, data };
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await _webView.InvokeScript($"window.frames[0].postMessage({responseJson}, {JsonSerializer.Serialize(_origin)});");
    }

    private async Task SendErrorAsync(string id, string error)
    {
        if (_origin is null) return;
        var response = new { type = "res", id, error };
        var responseJson = JsonSerializer.Serialize(response, JsonOptions);
        await _webView.InvokeScript($"window.frames[0].postMessage({responseJson}, {JsonSerializer.Serialize(_origin)});");
    }

    private async Task SendIframeRequestAsync(string id, string method, string data)
    {
        if (_origin is null) return;
        var request = new { type = "req", id, method, data };
        var json = JsonSerializer.Serialize(request, JsonOptions);
        await _webView.InvokeScript($"window.frames[0].postMessage({json}, {JsonSerializer.Serialize(_origin)});");
    }

    private async Task RunSmokeWatchdogAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
            if (!_smokeCompleted) FinishSmoke(false, "Timed out waiting for NativeWebView messages.");
        }
        catch (OperationCanceledException) { }
    }

    private void FinishSmoke(bool success, string message, Exception? exception = null)
    {
        if (_smokeCompleted) return;
        _smokeCompleted = true;
        _smokeSuccess = success;
        _smokeMessage = message;
        _smokeError = exception is null ? null : new SmokeError(exception.GetType().Name, exception.Message);
        SetStatus(message);
        WriteSmokeLog();

        if (_smokeMode)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(Close);
            });
        }
    }

    private void WriteSmokeLog()
    {
        if (_smokeLogPath is null) return;
        try
        {
            var output = new SmokeLog(
                _smokeSuccess,
                _smokeMessage,
                DateTimeOffset.UtcNow,
                _smokeStarted,
                _origin,
                _webMessageReceived,
                _validMessageCount,
                _messageCount,
                _validResponseCount,
                _invalidMessageCount,
                _errorMessageCount,
                _pageMessageSeen,
                _assetSeen,
                _imageResourceServed,
                _audioAssetSeen,
                _audioResourceServed,
                _internalLinkSeen,
                _themeMessageSeen,
                _viewerDarkThemeSeen,
                _historyMessageSeen,
                _linkResponseSeen,
                _backResponseSeen,
                _bridge?.History.Count ?? 0,
                _bridge?.IsDarkTheme ?? false,
                _transportIdempotent,
                _transportConflictObserved,
                _transportStopped,
                _transportRestarted,
                _transportClosed,
                _tempRootCleaned,
                _webView2,
                _platform,
                _smokeError);
            var full = Path.GetFullPath(_smokeLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, JsonSerializer.Serialize(output, JsonOptions));
        }
        catch (Exception ex)
        {
            SetStatus($"Smoke log error: {ex.Message}");
        }
    }

    private void WriteProbeLog()
    {
        if (_probeLogPath is null) return;
        try
        {
            var output = new PlatformProbeLog(
                DateTimeOffset.UtcNow,
                _origin,
                _webView2,
                _platform,
                _transportIdempotent,
                _transportConflictObserved,
                _transportStopped,
                _transportRestarted,
                _transportClosed,
                _tempRootCleaned,
                _smokeCompleted ? _smokeSuccess : null);
            var full = Path.GetFullPath(_probeLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, JsonSerializer.Serialize(output, JsonOptions));
        }
        catch (Exception ex)
        {
            SetStatus($"Probe log error: {ex.Message}");
        }
    }

    private async Task SelectFileAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, Title = "Select a probe input" });
            if (files.Count == 0)
            {
                _platform.FilePickerOutcome = "cancelled";
                _platform.FilePickerSelection = null;
                SetStatus("File selection cancelled.");
            }
            else
            {
                _platform.FilePickerOutcome = "selected";
                _platform.FilePickerSelection = files[0].Path.LocalPath;
                SetStatus($"Selected file: {_platform.FilePickerSelection}");
            }
        }
        catch (Exception ex)
        {
            _platform.FilePickerOutcome = "error";
            _platform.FilePickerError = ex.Message;
            SetStatus($"File selection error: {ex.Message}");
        }
        WriteProbeLog();
    }

    private async Task SelectFolderAsync()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = "Select a probe folder" });
            if (folders.Count == 0)
            {
                _platform.FolderPickerOutcome = "cancelled";
                _platform.FolderPickerSelection = null;
                SetStatus("Folder selection cancelled.");
            }
            else
            {
                _platform.FolderPickerOutcome = "selected";
                _platform.FolderPickerSelection = folders[0].Path.LocalPath;
                SetStatus($"Selected folder: {_platform.FolderPickerSelection}");
            }
        }
        catch (Exception ex)
        {
            _platform.FolderPickerOutcome = "error";
            _platform.FolderPickerError = ex.Message;
            SetStatus($"Folder selection error: {ex.Message}");
        }
        WriteProbeLog();
    }

    private async Task ClipboardAsync()
    {
        const string expected = "Phase 00 clipboard probe";
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                _platform.ClipboardError = "Clipboard is unavailable.";
                SetStatus(_platform.ClipboardError);
                return;
            }

            await clipboard.SetTextAsync(expected);
            _platform.ClipboardWriteSucceeded = true;
            var actual = await clipboard.TryGetTextAsync();
            _platform.ClipboardReadbackSucceeded = string.Equals(actual, expected, StringComparison.Ordinal);
            SetStatus(_platform.ClipboardReadbackSucceeded == true
                ? "Clipboard write/readback completed."
                : "Clipboard write completed, but readback did not match.");
        }
        catch (Exception ex)
        {
            _platform.ClipboardError = ex.Message;
            SetStatus($"Clipboard error: {ex.Message}");
        }
        finally
        {
            WriteProbeLog();
        }
    }

    private Task OpenExternalAsync()
    {
        const string url = "https://terraria.wiki.gg/";
        _platform.ExternalRequestAttempted = true;
        _platform.ExternalUrl = url;
        try
        {
            // This path is only called by an explicit human button click. It
            // is never invoked by smoke mode and never bypasses confirmation
            // or a user's system browser policy.
            var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            _platform.ExternalRequestSucceeded = process is not null;
            SetStatus(_platform.ExternalRequestSucceeded == true
                ? "External link request handed to the system browser."
                : "External link request returned no process handle.");
        }
        catch (Exception ex)
        {
            _platform.ExternalRequestSucceeded = false;
            _platform.ExternalRequestError = ex.Message;
            SetStatus($"External link request failed: {ex.Message}");
        }
        WriteProbeLog();
        return Task.CompletedTask;
    }

    private void ToggleTheme()
    {
        var next = ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        RequestedThemeVariant = next;
        _platform.ThemeToggleRequested = true;
        _platform.ThemeVariant = next.ToString();
        _platform.ThemeApplied = ActualThemeVariant == next || RequestedThemeVariant == next;
        _ = SendIframeRequestAsync("theme-button", "ChangeTheme", next == ThemeVariant.Dark ? "True" : "False");
        SetStatus($"Theme changed to {next}; applied={_platform.ThemeApplied}.");
        WriteProbeLog();
    }

    private static ParsedArgs ParseArgs(string[] args)
    {
        var smoke = false;
        string? smokeLog = null;
        string? probeLog = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--smoke-log", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("--smoke-log requires a path.");
                smoke = true;
                smokeLog = args[++i];
            }
            else if (string.Equals(args[i], "--probe-log", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("--probe-log requires a path.");
                probeLog = args[++i];
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown option '{args[i]}'.");
            }
        }
        return new ParsedArgs(smoke, smokeLog, probeLog);
    }

    private static void ValidateLogPath(string? path, string label)
    {
        if (path is null) return;
        if (Path.GetFullPath(path).StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label} log must not be placed inside the application output directory.");
    }

    private static WebView2RuntimeEvidence ProbeWebView2Runtime()
    {
        try
        {
            var executableFolder = Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER");
            if (!string.IsNullOrWhiteSpace(executableFolder))
            {
                var executable = Path.Combine(executableFolder, "msedgewebview2.exe");
                var evidence = ReadWebView2Executable(executable, "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER");
                if (evidence.Available) return evidence;
            }

            var programFiles = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var root in programFiles)
            {
                var applicationRoot = Path.Combine(root, "Microsoft", "EdgeWebView", "Application");
                if (!Directory.Exists(applicationRoot)) continue;
                foreach (var versionDirectory in Directory.EnumerateDirectories(applicationRoot).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var evidence = ReadWebView2Executable(Path.Combine(versionDirectory, "msedgewebview2.exe"), "EdgeWebView application directory");
                    if (evidence.Available) return evidence;
                }
            }

            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var clients = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients");
                if (clients is null) continue;
                foreach (var name in clients.GetSubKeyNames())
                {
                    using var client = clients.OpenSubKey(name);
                    var displayName = client?.GetValue("name") as string;
                    var version = client?.GetValue("pv") as string;
                    if ((displayName?.Contains("WebView2 Runtime", StringComparison.OrdinalIgnoreCase) == true
                         || name.Contains("WebView2", StringComparison.OrdinalIgnoreCase))
                        && !string.IsNullOrWhiteSpace(version))
                        return new WebView2RuntimeEvidence(true, version, $"{hive}/{view} registry", null);
                }
            }

            return new WebView2RuntimeEvidence(false, null, null,
                "Microsoft Edge WebView2 Runtime was not found in the configured folder, application directory, or EdgeUpdate registry.");
        }
        catch (Exception ex)
        {
            return new WebView2RuntimeEvidence(false, null, null,
                $"WebView2 Runtime probe failed ({ex.GetType().Name}): {ex.Message}");
        }
    }

    private static WebView2RuntimeEvidence ReadWebView2Executable(string path, string source)
    {
        if (!File.Exists(path))
            return WebView2RuntimeEvidence.Missing;
        var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
        return string.IsNullOrWhiteSpace(version)
            ? new WebView2RuntimeEvidence(false, null, source, $"WebView2 executable exists but has no file version: {path}")
            : new WebView2RuntimeEvidence(true, version, source, null);
    }

    private static async Task AddAudioFixtureAsync(string databasePath, CancellationToken cancellationToken)
    {
        // OggS is enough to exercise the same DB-asset/MIME route as a real
        // audio file without shipping media content in the disposable PoC.
        var audio = "OggS\0phase00-audio-fixture"u8.ToArray();
        await Task.Run(() =>
        {
            using var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            connection.Execute("INSERT OR REPLACE INTO WikiAsset (FileName, Data, MimeType, LastModified) VALUES (?, ?, ?, ?)",
                "fixture.ogg", audio, "audio/ogg", "2026-08-20T00:00:00Z");
            connection.Execute("UPDATE WikiPage SET Content = replace(Content, ?, ?) WHERE Title = ?",
                "</p>", "</p><audio controls src=\"/src/fixture.ogg\"></audio>", "Terraria Wiki");
        }, cancellationToken);
    }

    private void SetStatus(string text)
    {
        if (!CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SetStatus(text));
            return;
        }
        _status.Text = text;
    }

    private void Cleanup()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0) return;
        _platform.WindowClosed = true;
        _platform.WindowWidth = Width;
        _platform.WindowHeight = Height;
        string? tempRoot = _tempRoot;
        try { _lifetime.Cancel(); } catch { }
        if (_server is not null)
        {
            try
            {
                _server.StopAsync().GetAwaiter().GetResult();
                _transportStopped = !_server.IsRunning;
                var restarted = _server.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                _transportRestarted = restarted.Running;
                _server.StopAsync().GetAwaiter().GetResult();
                _transportClosed = !_server.IsRunning;
            }
            catch (Exception ex)
            {
                _platform.TransportCleanupError = ex.Message;
            }
        }
        _server = null;
        TryDeleteDirectory(tempRoot);
        _tempRootCleaned = tempRoot is null || !Directory.Exists(tempRoot);
        _tempRoot = null;
        WriteSmokeLog();
        WriteProbeLog();
        _lifetime.Dispose();
    }

    private static void TryDeleteDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { /* cleanup is best effort after the smoke result is written */ }
    }

    protected override void OnClosed(EventArgs e)
    {
        Cleanup();
        base.OnClosed(e);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private sealed record OuterWebMessage(string Source, string Origin, JsonElement Envelope);
    private sealed record ParsedArgs(bool Smoke, string? SmokeLogPath, string? ProbeLogPath);
    private sealed record WebView2RuntimeEvidence(bool Available, string? Version, string? Source, string? Error)
    {
        public static WebView2RuntimeEvidence Unknown { get; } = new(false, null, null, "not-probed");
        public static WebView2RuntimeEvidence Missing { get; } = new(false, null, null, "executable-not-found");
    }
    private sealed class PlatformProbeChecklist
    {
        public bool WindowOpened { get; set; }
        public bool WindowClosed { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public bool? WebView2Available { get; set; }
        public string? WebView2Version { get; set; }
        public string? WebView2Error { get; set; }
        public bool? ClipboardWriteSucceeded { get; set; }
        public bool? ClipboardReadbackSucceeded { get; set; }
        public string? ClipboardError { get; set; }
        public string? FilePickerOutcome { get; set; }
        public string? FilePickerSelection { get; set; }
        public string? FilePickerError { get; set; }
        public string? FolderPickerOutcome { get; set; }
        public string? FolderPickerSelection { get; set; }
        public string? FolderPickerError { get; set; }
        public bool ExternalRequestAttempted { get; set; }
        public bool? ExternalRequestSucceeded { get; set; }
        public string? ExternalUrl { get; set; }
        public string? ExternalRequestError { get; set; }
        public bool ThemeToggleRequested { get; set; }
        public string? ThemeVariant { get; set; }
        public bool? ThemeApplied { get; set; }
        public string? TransportCleanupError { get; set; }
    }
    private sealed record SmokeLog(bool Success, string Message, DateTimeOffset CompletedUtc, DateTimeOffset StartedUtc, string? Origin, bool WebMessageReceived, int ValidMessageCount, int MessageCount, int ValidResponseCount, int InvalidMessageCount, int ErrorMessageCount, bool PageRedirect, bool DatabaseAsset, bool ImageResourceServed, bool AudioAsset, bool AudioResourceServed, bool InternalLink, bool ChangeTheme, bool ViewerDarkTheme, bool SaveToTabHistory, bool InternalNavigationResponse, bool BackNavigationResponse, int HistoryCount, bool DarkTheme, bool TransportIdempotent, bool TransportConflictObserved, bool TransportStopped, bool TransportRestarted, bool TransportClosed, bool TempRootCleaned, WebView2RuntimeEvidence WebView2, PlatformProbeChecklist Platform, SmokeError? Error);
    private sealed record PlatformProbeLog(DateTimeOffset RecordedUtc, string? Origin, WebView2RuntimeEvidence WebView2, PlatformProbeChecklist Platform, bool TransportIdempotent, bool TransportConflictObserved, bool TransportStopped, bool TransportRestarted, bool TransportClosed, bool TempRootCleaned, bool? SmokeSuccess);
    private sealed record SmokeError(string Type, string Message);
}
