using System.Diagnostics;
using Terraria_Wiki.Core.Operations;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Favorites;
using Terraria_Wiki.Windows.Presentation.Management;
using Terraria_Wiki.Windows.Presentation.QuickAccess;
using Terraria_Wiki.Windows.Presentation.Reader;
using Terraria_Wiki.Windows.Presentation.Shell;
using Terraria_Wiki.Windows.Updates;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows;

internal sealed partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int value,
        int valueSize);

    private static readonly Uri WebView2DownloadUri = new(
        "https://developer.microsoft.com/microsoft-edge/webview2/",
        UriKind.Absolute);

    private readonly string[] _arguments;
    private string[] _runtimeArguments;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _smokeLogPath;
    private readonly string? _hiddenWebViewPocLogPath;
    private readonly string? _hiddenWebViewPocMode;
    private readonly string? _chatFavoritesPath;
    private readonly string? _diagnosticsDirectory;
    private readonly string? _aiSmokeQuestion;
    private readonly bool _aiSmokeCancelFirst;
    private readonly Stopwatch _lifecycle = Stopwatch.StartNew();
    private readonly WindowsLifecycleTimings _timings = new();
    private readonly long _processStartOffsetMilliseconds;
    private readonly long _windowConstructedMilliseconds;
    private readonly ShellViewModel _shellViewModel;
    private readonly ShellView _shellView;
    private readonly IChatConversationStore _conversationStore;
    private readonly HostSettingsStore _hostSettingsStore;
    private readonly UiTextCatalog _uiText = new();
    private readonly IProductUpdateService _productUpdateService;
    private readonly ProductDiagnostics _diagnostics;
    private WindowsClientRuntime? _runtime;
    private ViewerWebViewSession? _viewerSession;
    private QuickAccessWindow? _quickWindow;
    private GlobalHotkeys? _globalHotkeys;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _trayMainItem;
    private NativeMenuItem? _traySettingsItem;
    private NativeMenuItem? _trayExitItem;
    private QuickAccessPreferences _quickPreferences = new();
    private bool _quickRuntimeReady;
    private bool _forceExit;
    private bool _cleanupComplete;
    private bool _closing;
    private bool _isRestarting;
    private WindowsSmokeResult? _smokeResult;
    private Task _aiStartupTask = Task.CompletedTask;
    private long _viewerNavigationStartedMilliseconds;

    public MainWindow(string[] arguments)
    {
        WindowsStartupClock.MarkManagedEntry();
        _arguments = arguments ?? [];
        _runtimeArguments = _arguments.ToArray();
        _smokeLogPath = ParseOption(_arguments, "--smoke-log");
        _hiddenWebViewPocLogPath = ParseOption(_arguments, "--hidden-webview-poc-log");
        _hiddenWebViewPocMode = _arguments.Contains("--hidden-webview-poc-early", StringComparer.Ordinal)
            ? "early"
            : _arguments.Contains("--hidden-webview-poc-cancel", StringComparer.Ordinal) ? "cancel" : null;
        _chatFavoritesPath = ParseOption(_arguments, "--chat-favorites");
        _diagnosticsDirectory = ParseOption(_arguments, "--diagnostics-directory");
        _aiSmokeQuestion = ParseTextOption(_arguments, "--ai-smoke-question");
        _aiSmokeCancelFirst = HasFlag(_arguments, "--ai-smoke-cancel-first");
        _processStartOffsetMilliseconds = GetProcessStartOffsetMilliseconds();

        using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.MainWindowXaml))
            AvaloniaXamlLoader.Load(this);
        _uiText.Attach(Resources);
        _uiText.LanguageChanged += OnUiLanguageChanged;
        _shellView = this.FindControl<ShellView>("ShellHost")!;
        _productUpdateService = new VelopackProductUpdateService();
        using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.ProductDiagnosticsInitialization))
        {
            _diagnostics = _diagnosticsDirectory is null
                ? ProductDiagnostics.CreateDefault()
                : new ProductDiagnostics(_diagnosticsDirectory);
            _diagnostics.Write(ProductDiagnosticEvent.ApplicationStarted);
        }
        WindowsStartupInvestigation.Attach(_diagnostics);
        using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.ConversationStore))
            _conversationStore = CreateChatConversationStore(_chatFavoritesPath);
        string? hostSettingsPath;
        try { hostSettingsPath = ParseOption(_arguments, "--host-settings"); }
        catch (ArgumentException) { hostSettingsPath = null; } // Runtime reports malformed CLI options through startup recovery.
        using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.HostSettingsStore))
        {
            _hostSettingsStore = hostSettingsPath is null
                ? HostSettingsStore.CreateDefault()
                : new HostSettingsStore(hostSettingsPath);
        }
        using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.ShellViewModel))
        {
            _shellViewModel = new ShellViewModel(
                _conversationStore,
                PickImportDatabaseAsync,
                ExportDatabaseAsync,
                ApplyPreferencesAsync,
                LoadAiConfigurationDraftAsync,
                SaveAiConfigurationAsync,
                RestartAiAsync,
                ClearAiCredentialAsync,
                DiscoverAiModelsAsync,
                TestAiConfigurationAsync,
                _productUpdateService,
                ApplyUpdateAndRestartAsync,
                OpenExternalUriAsync,
                _diagnostics,
                LoadAiModelCatalogAsync,
                SaveAiModelCatalogAsync,
                _hostSettingsStore.LoadQuickAccessAsync,
                SaveQuickAccessAsync,
                suspended => _globalHotkeys?.SetSuspendedAsync(suspended) ?? Task.FromResult(true));
            _shellViewModel.UseTextCatalog(_uiText);
        }
        using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.ShellDataContext))
            _shellView.DataContext = _shellViewModel;
        _shellViewModel.RetryStartupRequested += OnRetryStartupRequested;
        _shellViewModel.GetWebView2Requested += OnGetWebView2Requested;
        _shellViewModel.Management.DataChanged += OnWikiDataChanged;
        _shellView.WebView.NavigationCompleted += OnViewerNavigationCompleted;
        SingleInstanceActivation.SetHandler(ShowMain);

        Opened += OnOpened;
        Closing += OnClosing;
        ActualThemeVariantChanged += (_, _) => ApplyNativeWindowTheme();

        var managedEntry = WindowsStartupClock.ManagedEntryMilliseconds;
        RecordStartupStage(
            WindowsStartupStage.ManagedEntry,
            managedEntry,
            managedEntry);
        _windowConstructedMilliseconds = ElapsedFromProcessStart();
        RecordStartupStage(
            WindowsStartupStage.WindowConstructed,
            _windowConstructedMilliseconds,
            Math.Max(0, _windowConstructedMilliseconds - managedEntry));
    }

    private static IChatConversationStore CreateChatConversationStore(string? legacyFavoritesPath)
    {
        try
        {
            if (legacyFavoritesPath is null)
                return ChatConversationStore.CreateDefault();
            var directory = Path.Combine(Path.GetDirectoryName(legacyFavoritesPath)!, "conversations");
            return new ChatConversationStore(directory, legacyFavoritesPath);
        }
        catch
        {
            return new MemoryChatConversationStore();
        }
    }

    private async Task InitializeQuickHostAsync()
    {
        using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.QuickPreferences))
        {
            try { _quickPreferences = await _hostSettingsStore.LoadQuickAccessAsync(_lifetime.Token); }
            catch { _quickPreferences = new QuickAccessPreferences(); }
        }
        try
        {
            using var trayTiming = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.TrayInitialization);
            using var iconStream = AssetLoader.Open(new Uri("avares://Terraria_Wiki.Windows/Assets/appicon.ico"));
            var menu = new NativeMenu();
            var mainItem = _trayMainItem = new NativeMenuItem(_uiText.Get("OpenMainWindow"));
            mainItem.Click += (_, _) => ShowMain();
            var quickItem = new NativeMenuItem("Quick Ask");
            quickItem.Click += (_, _) => ShowQuick();
            var settingsItem = _traySettingsItem = new NativeMenuItem(_uiText.Get("Settings"));
            settingsItem.Click += (_, _) => { ShowMain(); _shellViewModel.NavigateSettings(); };
            var exitItem = _trayExitItem = new NativeMenuItem(_uiText.Get("Exit"));
            exitItem.Click += async (_, _) => await ExitAsync();
            menu.Items.Add(mainItem);
            menu.Items.Add(quickItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exitItem);
            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(iconStream),
                ToolTipText = "Terraria AI Wiki",
                Menu = menu,
                IsVisible = true,
            };
            _trayIcon.Clicked += (_, _) => ShowQuick();
            TrayIcon.SetIcons(Avalonia.Application.Current!, new TrayIcons { _trayIcon });
        }
        catch
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
            _diagnostics.Write(ProductDiagnosticEvent.TrayInitializationFailed);
        }
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var hotkeyTiming = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.HotkeyInitialization);
            _globalHotkeys = new GlobalHotkeys(() => ShowQuick(QuickAccessShowIntent.GlobalHotkey), HideQuick);
            if (!await _globalHotkeys.TrySetAsync(_quickPreferences.ShowHotkey, _quickPreferences.HideHotkey))
                _shellViewModel.SetStartupStatus(UiTextCatalog.Message("QuickAskShortcutsAreUnavailableOpenItFrom"));
        }
        catch
        {
            _globalHotkeys?.Dispose();
            _globalHotkeys = null;
        }
    }

    private async Task<bool> SaveQuickAccessAsync(
        QuickAccessPreferences preferences, CancellationToken cancellationToken)
    {
        preferences.Validate();
        if (!GlobalHotkey.TryParse(preferences.ShowHotkey, out var show)
            || !GlobalHotkey.TryParse(preferences.HideHotkey, out var hide)
            || (show.Key != 0 && show == hide))
            return false;
        var previous = _quickPreferences;
        if (_globalHotkeys is not null
            && !await _globalHotkeys.TrySetAsync(preferences.ShowHotkey, preferences.HideHotkey))
            return false;
        try
        {
            await _hostSettingsStore.SaveQuickAccessAsync(preferences, cancellationToken);
            _quickPreferences = preferences;
            if (_quickWindow is { } window) window.ApplyTopmostPreference(preferences.Topmost);
            return true;
        }
        catch
        {
            if (_globalHotkeys is not null)
                await _globalHotkeys.TrySetAsync(previous.ShowHotkey, previous.HideHotkey);
            throw;
        }
    }

    private void ShowMain()
    {
        if (_closing || _cleanupComplete) return;
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ShowQuick(QuickAccessShowIntent intent = QuickAccessShowIntent.Tray)
    {
        if (_closing || _cleanupComplete) return;
        var clock = Stopwatch.StartNew();
        try
        {
            var created = _quickWindow is null;
            if (_quickWindow is null)
            {
                var quick = new QuickAccessWindow(_conversationStore,
                    _diagnostics, clock, _uiText);
                quick.HiddenByUser += OnQuickHidden;
                PlaceQuickWindow(quick, restoreSavedBounds: true);
                quick.RequestedThemeVariant = RequestedThemeVariant;
                _quickWindow = quick;
            }
            var window = _quickWindow;
            var wasVisible = window.IsVisible;
            if (!created && !wasVisible)
                PlaceQuickWindow(window, restoreSavedBounds: false);
            window.ShowQuick(_quickPreferences, intent);
            if (!created && !wasVisible)
                _diagnostics.WriteQuickAccessStage(QuickAccessStage.Reopened, clock.ElapsedMilliseconds);
            if (_quickRuntimeReady && _runtime is { } runtime)
                _ = window.AttachRuntimeAsync(runtime);
        }
        catch
        {
            _shellViewModel.SetStartupStatus(UiTextCatalog.Message("CouldNotOpenQuickAskTheMainWindow"), isError: true);
        }
    }

    private void HideQuick() => _quickWindow?.HideQuick();

    private void PlaceQuickWindow(QuickAccessWindow quick, bool restoreSavedBounds)
    {
        var targetX = restoreSavedBounds ? _quickPreferences.X : quick.Position.X;
        var targetY = restoreSavedBounds ? _quickPreferences.Y : quick.Position.Y;
        var savedScreen = targetX is { } x && targetY is { } y
            ? Screens.ScreenFromPoint(new PixelPoint(x, y)) : null;
        var screen = savedScreen ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var area = screen?.WorkingArea;
        var scale = screen?.Scaling ?? 1;
        if (restoreSavedBounds)
        {
            quick.Width = _quickPreferences.Width;
            quick.Height = _quickPreferences.Height;
        }
        else
        {
            quick.Width = Math.Max(quick.Bounds.Width, 420);
            quick.Height = Math.Max(quick.Bounds.Height, 360);
        }
        if (area is null) return;
        var bounds = area.Value;
        var pixelWidth = (int)Math.Round(quick.Width * scale);
        var pixelHeight = (int)Math.Round(quick.Height * scale);
        var preferredX = targetX ?? bounds.X + (bounds.Width - pixelWidth) / 2;
        var preferredY = targetY ?? bounds.Bottom - pixelHeight - 24;
        quick.Position = new PixelPoint(
            Math.Clamp(preferredX, bounds.X, Math.Max(bounds.X, bounds.Right - pixelWidth)),
            Math.Clamp(preferredY, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - pixelHeight)));
    }

    private async void OnQuickHidden(object? sender, EventArgs args)
    {
        if (sender is not QuickAccessWindow window || _closing) return;
        try { await SaveQuickBoundsAsync(window, _lifetime.Token); }
        catch { }
    }

    private async Task SaveQuickBoundsAsync(QuickAccessWindow window, CancellationToken cancellationToken)
    {
        var updated = _quickPreferences with
        {
            X = window.Position.X,
            Y = window.Position.Y,
            Width = Math.Max(window.Bounds.Width, 420),
            Height = Math.Max(window.Bounds.Height, 360),
        };
        await _hostSettingsStore.SaveQuickAccessAsync(updated, cancellationToken);
        _quickPreferences = updated;
    }

    private async Task ExitAsync()
    {
        if (_closing) return;
        _forceExit = true;
        Close();
        await Task.CompletedTask;
    }

    private async void OnOpened(object? sender, EventArgs args)
    {
        // Avalonia raises Opened again after Hide/Show. Restoring from the tray must
        // reuse the current runtime, not reacquire our own single-instance lock.
        Opened -= OnOpened;
        try
        {
            var preferences = await _hostSettingsStore.LoadPreferencesAsync(_lifetime.Token);
            _uiText.SetLanguage(preferences?.Language ?? ApplicationPreferences.DefaultLanguage);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch { } // Existing startup recovery reports invalid settings; do not overwrite them.
        ApplyNativeWindowTheme();
        var opened = ElapsedFromProcessStart();
        RecordStartupStage(
            WindowsStartupStage.WindowOpened,
            opened,
            Math.Max(0, opened - _windowConstructedMilliseconds));
        WindowsStartupInvestigation.Checkpoint(StartupInvestigationPhase.WindowOpened);
        WindowsStartupInvestigation.StartPersistence();
        if (_smokeLogPath is null && _hiddenWebViewPocLogPath is null)
        {
            using var quickTiming = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.QuickHostInitialization);
            await InitializeQuickHostAsync();
        }
        await StartAsync();
    }

    private async void OnRetryStartupRequested(object? sender, EventArgs args) => await RetryStartupAsync();

    private async void OnGetWebView2Requested(object? sender, EventArgs args) =>
        await OpenWebView2DownloadAsync();

    private async Task OpenWebView2DownloadAsync()
    {
        _diagnostics.Write(ProductDiagnosticEvent.WebView2RecoveryOpened);
        await OpenExternalUriAsync(WebView2DownloadUri);
    }

    private async void OnWikiDataChanged(object? sender, WikiDataChangedEventArgs args)
    {
        _shellViewModel.RefreshAfterWikiDataChange(args.RequiresViewerRebind);
        if (!args.RequiresViewerRebind || _runtime is null)
            return;
        var operationId = Guid.NewGuid();
        var stage = "viewer-rebind";
        _diagnostics.WriteWikiOperation(operationId, null, "started", new WikiProgress(stage));
        try
        {
            stage = "viewer-rebind-navigation";
            _diagnostics.WriteWikiOperation(operationId, null, "progress", new WikiProgress(stage));
            await RebindViewerAsync(_runtime, _lifetime.Token);
            stage = "viewer-rebind-quick";
            if (_quickWindow is { } quick) await quick.RebindAsync(resetContent: true);
            _diagnostics.WriteWikiOperation(operationId, null, "completed", new WikiProgress(stage));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _diagnostics.WriteWikiOperation(operationId, null, "failed", new WikiProgress(stage), exception,
                result: _viewerSession?.LastBridgeErrorCode);
            _shellViewModel.SetStartupStatus(
                UiTextCatalog.Message("WikiDataChangedButTheReaderCouldNot"),
                isError: true);
        }
    }

    private async Task StartAsync()
    {
        _shellViewModel.SetStartupStatus(UiTextCatalog.Message("CheckingWebViewTheWikiDataDirectoryAndLocal"));
        try
        {
            WindowsClientRuntime runtime;
            using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.RuntimeCreation))
                runtime = await WindowsClientRuntime.CreateAsync(
                    _runtimeArguments,
                    IsDarkTheme,
                    timing => RecordStartupStage(timing.Stage, timing.DurationMilliseconds),
                    _lifetime.Token,
                    _hostSettingsStore);
            _runtime = runtime;
            _uiText.SetLanguage(runtime.Preferences.Language);
            _shellViewModel.SetWebView2Status(runtime.WebView2);
            _diagnostics.Write(ProductDiagnosticEvent.RuntimeReady, runtime.WebView2.Version);
            _timings.RuntimeReadyMilliseconds = ElapsedFromProcessStart();
            RecordStartupStage(
                WindowsStartupStage.RuntimeReady,
                _timings.RuntimeReadyMilliseconds,
                0);
            runtime.Viewer.StatusChanged += OnViewerStatusChanged;
            using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.ThemeApplication))
            {
                Classes.Set("interaction-feedback", runtime.Preferences.IsInteractionFeedbackEnabled);
                RequestedThemeVariant = runtime.Preferences.AppTheme switch
                {
                    "dark" => ThemeVariant.Dark,
                    "light" => ThemeVariant.Light,
                    _ => ThemeVariant.Default,
                };
                ApplyNativeWindowTheme();
                if (_quickWindow is { } themedQuick)
                    themedQuick.RequestedThemeVariant = RequestedThemeVariant;
            }

            var viewerTimer = Stopwatch.StartNew();
            try
            {
                await runtime.Viewer.EnableAsync(_lifetime.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw WrapViewerServerFailure(exception);
            }
            finally
            {
                viewerTimer.Stop();
                RecordStartupStage(WindowsStartupStage.ViewerServer, viewerTimer.ElapsedMilliseconds);
            }
            _timings.ViewerServerReadyMilliseconds = ElapsedFromProcessStart();

            var session = new ViewerWebViewSession(_shellView.WebView, runtime.Bridge);
            _viewerSession = session;
            session.StatusChanged += OnViewerSessionStatusChanged;
            session.ProtocolError += OnViewerProtocolError;
            session.ReadyChanged += OnViewerReadyChanged;
            session.PageCommitted += OnViewerPageCommitted;
            session.HistoryChanged += OnViewerHistoryChanged;

            if (runtime.Viewer.Status is not { State: ViewerConsumerState.Ready, BaseUri: not null } ready)
                throw new WindowsClientStartupException(
                    "viewer-not-ready",
                    runtime.Viewer.Status.Message ?? "Viewer is not ready.");

            using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.RuntimeAttachment))
                _shellViewModel.AttachRuntime(runtime, SwitchWikiAndRebindAsync);
            WikiBookSnapshot activeWiki;
            using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.ActiveWiki))
                activeWiki = await runtime.Application.ActiveWiki.GetActiveAsync(_lifetime.Token);
            _shellViewModel.SetWikiHomePage(activeWiki.DefaultPageTitle);
            _quickRuntimeReady = true;
            if (_quickWindow is { } quickWindow)
                _ = quickWindow.AttachRuntimeAsync(runtime);
            if (!activeWiki.IsPageDownloaded)
            {
                _shellViewModel.ShowEmptyWikiOnboarding();
                using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AiDispatch))
                    _aiStartupTask = EnableAiInBackgroundAsync(runtime);
                StartHiddenWebViewPoc(runtime);
                return;
            }

            _shellViewModel.Reader.SetStatus(UiTextCatalog.Message("LoadingTheLocalWikiHomePage"));
            var navigationTimer = Stopwatch.StartNew();
            try
            {
                session.Navigate(ready.BaseUri, activeWiki.Id, activeWiki.Title);
                _viewerNavigationStartedMilliseconds = ElapsedFromProcessStart();
                RecordStartupStage(
                    WindowsStartupStage.ViewerNavigationStarted,
                    _viewerNavigationStartedMilliseconds,
                    navigationTimer.ElapsedMilliseconds);
            }
            catch (Exception exception)
            {
                throw new WindowsClientStartupException(
                    "viewer-navigation-failed",
                    "The local Viewer could not begin navigation.",
                    exception);
            }

            using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AiDispatch))
                _aiStartupTask = EnableAiInBackgroundAsync(runtime);
            var domTimer = Stopwatch.StartNew();
            try
            {
                await session.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), _lifetime.Token);
            }
            catch (TimeoutException exception)
            {
                throw new WindowsClientStartupException(
                    "viewer-dom-ready-timeout",
                    "The local Viewer did not become interactive in time.",
                    exception);
            }
            finally
            {
                domTimer.Stop();
                RecordStartupStage(WindowsStartupStage.ViewerDomReady, domTimer.ElapsedMilliseconds);
            }
            _timings.ViewerReadyMilliseconds = ElapsedFromProcessStart();
            _shellViewModel.AttachReader(session.OpenPageAsync, session.BackAsync, session.HasHistory);
            if (session.CurrentPage is { } currentPage)
                await _shellViewModel.Reader.ApplyPageIdentityAsync(currentPage);
            _shellViewModel.ApplyStartupPage(runtime.Preferences.StartupPage);
            _shellViewModel.SetStartupStatus(
                UiTextCatalog.Message("LocalWikiReadyWebView", runtime.WebView2.Version ?? "available"));
            StartHiddenWebViewPoc(runtime);

            if (_smokeLogPath is not null)
            {
                _shellViewModel.OpenWikiReader();
                _ = RunSmokeAsync();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (exception is HostMountException { Code: "client-already-running" }
                && SingleInstanceActivation.TryNotifyExisting())
            {
                await ExitAsync();
                return;
            }
            if (_hiddenWebViewPocLogPath is { } logPath)
                File.AppendAllText(logPath, JsonSerializer.Serialize(new { stage = "startup-failed", type = exception.GetType().FullName, exception.Message }) + Environment.NewLine);
            await DisposeRuntimeAsync();
            _quickWindow?.MarkHostUnavailable();
            ShowStartupError(exception);
            if (_smokeLogPath is not null)
            {
                _smokeResult = WindowsSmokeResult.Failure(GetSafeErrorCode(exception), _timings);
                Close();
            }
        }
    }

    private void StartHiddenWebViewPoc(WindowsClientRuntime runtime)
    {
        if (_hiddenWebViewPocLogPath is { } logPath)
            _ = HiddenWebViewPoc.RunAsync(this, runtime, logPath, _hiddenWebViewPocMode, _lifetime.Token);
    }

    private async Task RetryStartupAsync()
    {
        if (_closing || _isRestarting)
            return;
        _isRestarting = true;
        try
        {
            await DisposeRuntimeAsync();
            await StartAsync();
        }
        finally
        {
            _isRestarting = false;
        }
    }

    private bool IsDarkTheme() =>
        RequestedThemeVariant == ThemeVariant.Dark
        || (RequestedThemeVariant == ThemeVariant.Default && ActualThemeVariant == ThemeVariant.Dark);

    private void ApplyNativeWindowTheme()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = TryGetPlatformHandle()?.Handle;
        if (handle is null || handle == 0)
            return;

        var isDark = IsDarkTheme();
        var darkMode = isDark ? 1 : 0;
        var captionColor = isDark
            ? NativeThemeColor("TerrariaDarkNavigationColor")
            : ToColorRef(0xFF, 0xFF, 0xFF);
        var borderColor = isDark
            ? NativeThemeColor("TerrariaDarkStrokeColor")
            : ToColorRef(0xDC, 0xE5, 0xD7);
        var textColor = isDark
            ? NativeThemeColor("TerrariaDarkTextColor")
            : ToColorRef(0x00, 0x00, 0x00);

        try
        {
            var darkModeResult = DwmSetWindowAttribute(
                handle.Value,
                DwmwaUseImmersiveDarkMode,
                ref darkMode,
                sizeof(int));
            if (darkModeResult != 0)
            {
                _ = DwmSetWindowAttribute(
                    handle.Value,
                    DwmwaUseImmersiveDarkModeLegacy,
                    ref darkMode,
                    sizeof(int));
            }

            _ = DwmSetWindowAttribute(
                handle.Value,
                DwmwaCaptionColor,
                ref captionColor,
                sizeof(int));
            _ = DwmSetWindowAttribute(
                handle.Value,
                DwmwaBorderColor,
                ref borderColor,
                sizeof(int));
            _ = DwmSetWindowAttribute(
                handle.Value,
                DwmwaTextColor,
                ref textColor,
                sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private int NativeThemeColor(string key) =>
        this.TryFindResource(key, out var resource) && resource is Avalonia.Media.Color color
            ? ToColorRef(color.R, color.G, color.B)
            : -1; // DWM_COLOR_DEFAULT if theme resources are unavailable.

    private static int ToColorRef(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    private void OnViewerNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        _timings.ViewerNavigationCompletedMilliseconds = ElapsedFromProcessStart();
        RecordStartupStage(
            WindowsStartupStage.ViewerNavigationCompleted,
            _timings.ViewerNavigationCompletedMilliseconds,
            Math.Max(0, _timings.ViewerNavigationCompletedMilliseconds - _viewerNavigationStartedMilliseconds));
        _shellViewModel.Reader.SetStatus(UiTextCatalog.Message("PreparingLocalWikiContent"));
    }

    private void OnViewerSessionStatusChanged(object? sender, string message) =>
        Dispatcher.UIThread.Post(() => _shellViewModel.Reader.SetStatus(message));

    private void OnViewerProtocolError(object? sender, ViewerProtocolError error) =>
        Dispatcher.UIThread.Post(() => _shellViewModel.Reader.SetStatus(UiTextCatalog.Message("ViewerError", error.Message)));

    private void OnViewerReadyChanged(object? sender, bool isReady) =>
        Dispatcher.UIThread.Post(() => _shellViewModel.Reader.SetTransportReady(isReady));

    private void OnViewerStatusChanged(object? sender, ViewerConsumerStatus status) =>
        Dispatcher.UIThread.Post(() =>
        {
            _shellViewModel.Reader.SetStatus(
                status.Error is null
                    ? status.Message ?? status.State.ToString()
                    : UiTextCatalog.Message("ViewerTemporarilyUnavailable"));
            if (status.State == ViewerConsumerState.Failed)
                _shellViewModel.SetStartupStatus(UiTextCatalog.Message("ViewerFailedToStartResolveTheIssueAnd"), isError: true);
        });

    private void ShowStartupError(Exception exception)
    {
        var presentation = ClassifyStartupFailure(exception);
        _diagnostics.Write(ProductDiagnosticEvent.StartupFailed, GetSafeErrorCode(exception));
        _shellViewModel.SetStartupStatus(
            UiTextCatalog.Join("", UiTextCatalog.FromSafeMessage(presentation.Message), UiTextCatalog.Message("ResolveTheIssueThenChooseRetry")),
            isError: true,
            presentation.Recovery);
    }

    internal static StartupFailurePresentation ClassifyStartupFailure(Exception exception) => exception switch
    {
        WindowsClientStartupException { Code: "webview2-missing" } => new(
            StartupRecoveryKind.InstallWebView2,
            UiTextCatalog.Message("MicrosoftEdgeWebViewRuntimeWasNotDetectedInstall")),
        DataRootResolutionException { Code: "data-root-required" } => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("TheWikiDataDirectoryIsCurrentlyUnavailableRestore")),
        DataRootResolutionException { Code: "default-data-root-unavailable" } => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("CouldNotCreateTheDefaultWikiDataDirectory")),
        DataRootResolutionException => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("TheWikiDataDirectoryIsUnavailableRestoreThe")),
        ApplicationUseCaseException { Code: "data-directory-conflict" } => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("WikiDataExistsInBothTheOldAnd")),
        ApplicationUseCaseException { Code: "database-family-conflict" } => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("ConflictingWikiDatabaseFilesWereDetectedTheyWere")),
        ApplicationUseCaseException => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("CouldNotInitializeTheWikiDatabaseRestoreThe")),
        WindowsClientStartupException { Code: "viewer-assets-missing" } => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("LocalViewerAssetsAreMissingRepairTheApp")),
        WindowsClientStartupException { Code: "viewer-bind-failed" } => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("TheLocalViewerCouldNotBindALoopback")),
        WindowsClientStartupException { Code: "viewer-navigation-failed" } => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("TheLocalViewerCouldNotStartPageNavigation")),
        WindowsClientStartupException { Code: "viewer-dom-ready-timeout" } => new(
            StartupRecoveryKind.Retry,
            UiTextCatalog.Message("TheLocalViewerPageDidNotBecomeReady")),
        WindowsClientStartupException startup => new(StartupRecoveryKind.Retry, startup.Message),
        HostMountException mount => new(StartupRecoveryKind.Retry, mount.Message),
        HostSettingsException settings => new(StartupRecoveryKind.Retry, settings.Message),
        _ => new(StartupRecoveryKind.Retry, UiTextCatalog.Message("AppStartupFailed")),
    };

    internal static WindowsClientStartupException WrapViewerServerFailure(Exception exception) => exception switch
    {
        DirectoryNotFoundException or FileNotFoundException => new WindowsClientStartupException(
            "viewer-assets-missing",
            "The local Viewer assets are unavailable.",
            exception),
        ViewerServerStartException => new WindowsClientStartupException(
            "viewer-bind-failed",
            "The local Viewer could not bind its loopback endpoint.",
            exception),
        _ => new WindowsClientStartupException(
            "viewer-server-start-failed",
            "The local Viewer server could not start.",
            exception),
    };

    private static string GetSafeErrorCode(Exception exception) => exception switch
    {
        WindowsClientStartupException startup => startup.Code,
        DataRootResolutionException dataRoot => dataRoot.Code,
        HostSettingsException settings => settings.Code,
        HostMountException mount => mount.Code,
        ApplicationUseCaseException application => application.Code,
        _ => "unknown-startup-error",
    };

    private void RecordStartupStage(
        WindowsStartupStage stage,
        long durationMilliseconds)
    {
        var elapsed = ElapsedFromProcessStart();
        RecordStartupStage(stage, elapsed, durationMilliseconds);
    }

    private void RecordStartupStage(
        WindowsStartupStage stage,
        long elapsedMilliseconds,
        long durationMilliseconds)
    {
        WindowsStartupInvestigation.ExistingStage(stage, elapsedMilliseconds, durationMilliseconds);
        _timings.RecordStartupStage(stage, elapsedMilliseconds, durationMilliseconds);
        _diagnostics.WriteStartupStage(stage, elapsedMilliseconds, durationMilliseconds);
    }

    private async Task RunSmokeAsync()
    {
        var runtime = _runtime;
        var session = _viewerSession;
        if (runtime is null || session is null)
            return;

        try
        {
            await session.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), _lifetime.Token);
            var search = await runtime.Application.Query.SearchAsync("Smoke Target", 10, _lifetime.Token);
            if (search.Count == 0)
                throw new InvalidOperationException("Synthetic smoke search returned no results.");

            await _shellView.WebView.InvokeScript(
                "document.getElementById('wiki-frame')?.contentDocument?.querySelector('#mw-content-text a[title=\"Smoke Alias\"]')?.click()");
            var linkDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < linkDeadline
                   && !string.Equals(session.CurrentPage?.CanonicalTitle, "Smoke Target", StringComparison.Ordinal))
            {
                await Task.Delay(100, _lifetime.Token);
            }
            if (!string.Equals(session.CurrentPage?.CanonicalTitle, "Smoke Target", StringComparison.Ordinal))
                throw new InvalidOperationException("Viewer internal link click did not navigate to the target page.");
            await session.ChangeThemeAsync(isDark: true, _lifetime.Token);
            await session.BackAsync(_lifetime.Token);
            await session.ForwardAsync(_lifetime.Token);
            await _shellView.WebView.InvokeScript(
                "fetch('/src/smoke.ogg').then(r=>r.arrayBuffer()).then(b=>b.byteLength.toString())");

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline
                   && (runtime.Viewer.GetRequestCount("/src/smoke.png") == 0
                       || runtime.Viewer.GetRequestCount("/src/smoke.ogg") == 0
                       || runtime.Viewer.GetRequestCount("/wiki/Terraria_Wiki_zh/app.js") == 0
                       || runtime.Viewer.GetRequestCount("/wiki/Terraria_Wiki_zh/vector.css") == 0))
            {
                await Task.Delay(100, _lifetime.Token);
            }

            var aiSmoke = await RunAiSmokeAsync(runtime);
            var result = new WindowsSmokeResult(
                true,
                "Windows client smoke passed.",
                search.Count,
                runtime.Viewer.GetRequestCount("/wiki/Terraria_Wiki_zh/app.js"),
                runtime.Viewer.GetRequestCount("/wiki/Terraria_Wiki_zh/vector.css"),
                runtime.Viewer.GetRequestCount("/src/smoke.png"),
                runtime.Viewer.GetRequestCount("/src/smoke.ogg"),
                runtime.Bridge.History.Count,
                false,
                null,
                aiSmoke,
                new WindowsUpdateSmokeResult(
                    _productUpdateService.Capabilities.CanUpdate,
                    _productUpdateService.Capabilities.IsPortable,
                    _productUpdateService.Capabilities.CurrentVersion,
                    _productUpdateService.Capabilities.ReleasesUri is not null));
            if (result.ScriptRequests == 0
                || result.StyleRequests == 0
                || result.ImageRequests == 0
                || result.AudioRequests == 0)
            {
                throw new InvalidOperationException("Viewer CSS, JS, image, or audio was not requested.");
            }
            _smokeResult = result;
            _shellViewModel.SetStartupStatus(result.Message);
        }
        catch (Exception exception)
        {
            _smokeResult = WindowsSmokeResult.Failure(GetSafeErrorCode(exception));
            _shellViewModel.SetStartupStatus(UiTextCatalog.Message("SmokeFailed"), isError: true);
        }
        finally
        {
            Close();
        }
    }

    private async Task<WindowsAiSmokeResult?> RunAiSmokeAsync(WindowsClientRuntime runtime)
    {
        if (_aiSmokeQuestion is null)
            return null;
        await _aiStartupTask.WaitAsync(TimeSpan.FromSeconds(20), _lifetime.Token);
        var ai = runtime.Ai;
        if (ai is null || runtime.AiStatus.State != WindowsAiRuntimeState.Ready)
            throw new InvalidOperationException(
                $"AI smoke unavailable: {runtime.AiStatus.ErrorCode ?? "runtime-unavailable"}.");

        if (_aiSmokeCancelFirst)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var cancelled = ai.AnswerAsync(
                _aiSmokeQuestion,
                value =>
                {
                    if (value.Kind == AiRunEventKind.RunStarted)
                        cancellation.Cancel();
                    return ValueTask.CompletedTask;
                },
                cancellation.Token);
            try
            {
                await cancelled;
                throw new InvalidOperationException("AI smoke cancellation unexpectedly completed.");
            }
            catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
            {
            }
        }

        var timer = Stopwatch.StartNew();
        long firstDeltaMilliseconds = -1;
        var answerDeltas = 0;
        var toolEvents = 0;
        var sawSearch = false;
        var sawRead = false;
        var answer = await ai.AnswerAsync(
            _aiSmokeQuestion,
            value =>
            {
                if (value.Kind == AiRunEventKind.AnswerDelta)
                {
                    Interlocked.Increment(ref answerDeltas);
                    Interlocked.CompareExchange(ref firstDeltaMilliseconds, timer.ElapsedMilliseconds, -1);
                }
                else if (value.Kind is AiRunEventKind.ToolStarted or AiRunEventKind.ToolFinished)
                {
                    Interlocked.Increment(ref toolEvents);
                    if (value.Kind == AiRunEventKind.ToolStarted && value.Name == "search_wiki")
                        sawSearch = true;
                    if (value.Kind == AiRunEventKind.ToolStarted && value.Name == "read_wiki")
                        sawRead = true;
                }
                return ValueTask.CompletedTask;
            },
            _lifetime.Token);
        timer.Stop();
        if (answerDeltas < 2 || !sawSearch || !sawRead || answer.Sources.Count == 0)
        {
            throw new InvalidOperationException(
                "AI smoke did not produce the required tool, streaming, and source evidence.");
        }

        return new WindowsAiSmokeResult(
            true,
            _aiSmokeCancelFirst,
            answer.Status,
            answerDeltas,
            toolEvents,
            answer.Sources.Count,
            answer.Usage.InputTokens,
            answer.Usage.OutputTokens,
            answer.Usage.TotalTokens,
            firstDeltaMilliseconds,
            timer.ElapsedMilliseconds);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_cleanupComplete)
            return;
        args.Cancel = true;
        if (_closing)
            return;
        if (!_forceExit && _quickPreferences.CloseToTray && args.CloseReason == WindowCloseReason.WindowClosing)
        {
            if (_trayIcon is { IsVisible: true }) Hide();
            else
                _shellViewModel.SetStartupStatus(UiTextCatalog.Message("TheTrayIsUnavailableSoThisWindowWas"),
                    isError: true, recovery: StartupRecoveryKind.None);
            return;
        }
        _closing = true;
        await CleanupAsync();
        Close();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private async Task ApplyUpdateAndRestartAsync()
    {
        if (_closing)
            return;
        _forceExit = true;
        _closing = true;
        await CleanupAsync();
        _productUpdateService.ApplyAndRestart();
        Close();
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static Task OpenExternalUriAsync(Uri uri) =>
        new WindowsExternalUriLauncher().OpenAsync(uri);

    private async Task CleanupAsync()
    {
        var closeTimer = Stopwatch.StartNew();
        _timings.CloseRequestedMilliseconds = ElapsedFromProcessStart();
        _lifetime.Cancel();
        _shellViewModel.SetStartupStatus(UiTextCatalog.Message("ClosingAIViewerAndTheLocalDatabase"));
        _globalHotkeys?.Dispose();
        _globalHotkeys = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (_quickWindow is { } quickWindow)
        {
            _quickWindow = null;
            quickWindow.HiddenByUser -= OnQuickHidden;
            try { await SaveQuickBoundsAsync(quickWindow, CancellationToken.None); } catch { }
            await quickWindow.DisposeAsync();
        }
        await DisposeRuntimeAsync();
        _shellViewModel.Dispose();
        _shellViewModel.RetryStartupRequested -= OnRetryStartupRequested;
        _shellViewModel.GetWebView2Requested -= OnGetWebView2Requested;
        _shellViewModel.Management.DataChanged -= OnWikiDataChanged;
        _shellView.WebView.NavigationCompleted -= OnViewerNavigationCompleted;
        SingleInstanceActivation.SetHandler(null);
        _lifetime.Dispose();
        closeTimer.Stop();
        _timings.CloseTotalMilliseconds = closeTimer.ElapsedMilliseconds;
        _diagnostics.WriteShutdown(
            _timings.WebViewStopMilliseconds,
            _timings.AiStopMilliseconds,
            _timings.ViewerStopMilliseconds,
            _timings.DatabaseCloseMilliseconds,
            _timings.RuntimeDisposeMilliseconds,
            _timings.CloseTotalMilliseconds);
        _cleanupComplete = true;
        await WriteSmokeLogAsync();
    }

    private async Task DisposeRuntimeAsync()
    {
        _quickRuntimeReady = false;
        if (_quickWindow is { } quickWindow)
            await quickWindow.DetachRuntimeAsync();
        _shellViewModel.DetachRuntime();

        var session = _viewerSession;
        _viewerSession = null;
        var sessionStop = Task.CompletedTask;
        if (session is not null)
        {
            _timings.ViewerWebMessageCount = session.ReceivedMessageCount;
            _timings.ViewerValidMessageCount = session.ValidMessageCount;
            _timings.ViewerLastRequestMethod = session.LastRequestMethod;
            _timings.ViewerLastCompletedMethod = session.LastCompletedMethod;
            _timings.ViewerLastBridgeErrorCode = session.LastBridgeErrorCode;
            session.StatusChanged -= OnViewerSessionStatusChanged;
            session.ProtocolError -= OnViewerProtocolError;
            session.ReadyChanged -= OnViewerReadyChanged;
            session.PageCommitted -= OnViewerPageCommitted;
            session.HistoryChanged -= OnViewerHistoryChanged;
            sessionStop = DisposeViewerSessionAsync(session);
        }

        var runtime = _runtime;
        _runtime = null;
        if (runtime is not null)
        {
            _timings.ViewerOuterRequestCount = runtime.Viewer.GetRequestCount("/");
            _timings.ViewerIndexRequestCount = runtime.Viewer.GetRequestCount("/wiki/Terraria_Wiki_zh/index.html");
            _timings.ViewerCommonScriptRequestCount = runtime.Viewer.GetRequestCount("/_common/wiki-app-common.js");
            _timings.ViewerAppScriptRequestCount = runtime.Viewer.GetRequestCount("/wiki/Terraria_Wiki_zh/app.js");
            runtime.Viewer.StatusChanged -= OnViewerStatusChanged;
            try { await runtime.DisposeAsync(sessionStop); } catch { }
            if (_closing)
            {
                _timings.AiStopMilliseconds = runtime.DisposeTimings.AiStopMilliseconds;
                _timings.ViewerStopMilliseconds = runtime.DisposeTimings.ViewerStopMilliseconds;
                _timings.DatabaseCloseMilliseconds = runtime.DisposeTimings.ApplicationStopMilliseconds;
                _timings.RuntimeDisposeMilliseconds = runtime.DisposeTimings.TotalMilliseconds;
            }
        }
        await sessionStop;
    }

    private async Task DisposeViewerSessionAsync(ViewerWebViewSession session)
    {
        var timer = Stopwatch.StartNew();
        try { await session.DisposeAsync(); } catch { }
        timer.Stop();
        if (_closing)
            _timings.WebViewStopMilliseconds = timer.ElapsedMilliseconds;
    }

    private async Task EnableAiInBackgroundAsync(WindowsClientRuntime runtime)
    {
        _timings.AiDispatchMilliseconds = ElapsedFromProcessStart();
        try
        {
            await Task.Run(() => runtime.EnableAiAsync(_lifetime.Token), _lifetime.Token);
            if (runtime.AiStatus.State == WindowsAiRuntimeState.Ready)
                _timings.AiReadyMilliseconds = ElapsedFromProcessStart();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _timings.AiStartCompletedMilliseconds = ElapsedFromProcessStart();
        }
    }

    private async Task<string?> PickImportDatabaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _uiText.Get("SelectWikiDatabase"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(_uiText.Get("SQLiteDatabase")) { Patterns = ["*.db"] },
            ],
        });
        cancellationToken.ThrowIfCancellationRequested();
        var file = files.SingleOrDefault();
        return file?.Path is { IsFile: true } path ? path.LocalPath : null;
    }

    private Task<AiRuntimeConfigurationDraft> LoadAiConfigurationDraftAsync(
        CancellationToken cancellationToken) =>
        CreateAiConfigurationStore().LoadDraftAsync(cancellationToken);

    private Task<AiRuntimeConfiguration> SaveAiConfigurationAsync(
        AiRuntimeConfigurationDraft draft,
        CancellationToken cancellationToken) =>
        CreateAiConfigurationStore().SaveAsync(draft, cancellationToken);

    private async Task<WindowsAiRuntimeStatus> RestartAiAsync(CancellationToken cancellationToken)
    {
        var runtime = _runtime;
        if (runtime is null)
        {
            return new WindowsAiRuntimeStatus(
                WindowsAiRuntimeState.Disabled,
                UiTextCatalog.Message("AIConfigurationSavedAICanStartWhenThe"),
                "data-root-required");
        }
        await runtime.RetryAiAsync(cancellationToken);
        return runtime.AiStatus;
    }

    private async Task<WindowsAiRuntimeStatus> ClearAiCredentialAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CreateAiConfigurationStore().DeleteCredential();
        return await RestartAiAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> DiscoverAiModelsAsync(
        AiRuntimeConfigurationDraft draft,
        CancellationToken cancellationToken)
    {
        var store = CreateAiConfigurationStore();
        var apiKey = store.ResolveCredential(draft.ApiKey);
        using var client = new AiProviderConnectivityClient();
        return await client.DiscoverModelsAsync(draft.Provider, apiKey, cancellationToken);
    }

    private async Task TestAiConfigurationAsync(
        AiRuntimeConfigurationDraft draft,
        CancellationToken cancellationToken)
    {
        var store = CreateAiConfigurationStore();
        var apiKey = store.ResolveCredential(draft.ApiKey);
        using var client = new AiProviderConnectivityClient();
        await client.TestConfigurationAsync(draft.Provider, apiKey, cancellationToken);
    }

    private Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadAiModelCatalogAsync(
        CancellationToken cancellationToken)
    {
        var store = new AiModelCatalogStore(CreateAiConfigurationStore().ConfigurationPath);
        return store.LoadAsync(cancellationToken);
    }

    private Task SaveAiModelCatalogAsync(
        AiProviderConfiguration provider,
        IReadOnlyList<string> models,
        CancellationToken cancellationToken)
    {
        var store = new AiModelCatalogStore(CreateAiConfigurationStore().ConfigurationPath);
        return store.SaveAsync(provider, models, cancellationToken);
    }

    private AiRuntimeConfigurationStore CreateAiConfigurationStore()
    {
        var path = AiRuntimeConfiguration.ParseConfigurationPath(_runtimeArguments)
            ?? AiRuntimeConfiguration.DefaultConfigurationPath();
        return new AiRuntimeConfigurationStore(path);
    }

    private async Task<WikiDatabaseExportResponse?> ExportDatabaseAsync(
        WikiBookSnapshot wiki,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wiki);
        var runtime = _runtime;
        if (runtime is null)
            return null;
        cancellationToken.ThrowIfCancellationRequested();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _uiText.Get("ExportWikiDatabase"),
            SuggestedFileName = SanitizeFileName(wiki.Title) + "-" + DateTime.Now.ToString("yyyyMMdd") + ".db",
            DefaultExtension = "db",
            FileTypeChoices =
            [
                new FilePickerFileType(_uiText.Get("SQLiteDatabase")) { Patterns = ["*.db"] },
            ],
        });
        cancellationToken.ThrowIfCancellationRequested();
        if (file?.Path is not { IsFile: true } path)
            return null;

        var targetPath = Path.GetFullPath(path.LocalPath);
        var exportPath = File.Exists(targetPath) ? CreateTemporaryExportPath(targetPath) : targetPath;
        try
        {
            var result = await runtime.Application.Data.ExportDatabaseAsync(exportPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(exportPath, targetPath, StringComparison.OrdinalIgnoreCase))
                CompleteExportTarget(exportPath, targetPath);
            return result with { DatabasePath = targetPath };
        }
        finally
        {
            if (!string.Equals(exportPath, targetPath, StringComparison.OrdinalIgnoreCase))
                TryDeleteFile(exportPath);
        }
    }

    private async Task ApplyPreferencesAsync(ApplicationPreferences preferences)
    {
        _uiText.SetLanguage(preferences.Language);
        var runtime = _runtime;
        if (runtime is null)
            return;
        var previous = runtime.Preferences;
        runtime.ApplyPreferences(preferences);
        Classes.Set("interaction-feedback", preferences.IsInteractionFeedbackEnabled);
        RequestedThemeVariant = preferences.AppTheme switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
        ApplyNativeWindowTheme();

        if (!string.Equals(previous.ContentTheme, preferences.ContentTheme, StringComparison.Ordinal))
        {
            await runtime.Viewer.RetryAsync(_lifetime.Token);
            await RebindViewerAsync(runtime, _lifetime.Token);
            if (_quickWindow is { } quick) await quick.RebindAsync();
        }
        else if (!string.Equals(previous.AppTheme, preferences.AppTheme, StringComparison.Ordinal)
                 && preferences.ContentTheme == "auto"
                 && _viewerSession is { IsReady: true } session)
        {
            await session.ChangeThemeAsync(IsDarkTheme(), _lifetime.Token);
        }
        if (_quickWindow is { } widget)
        {
            widget.ApplySideButtonPreference(preferences.IsSideButtonBackEnabled);
            if (previous.AppTheme != preferences.AppTheme && preferences.ContentTheme == "auto")
                await widget.ApplyThemeAsync(RequestedThemeVariant, IsDarkTheme(), contentChanged: false);
            else widget.RequestedThemeVariant = RequestedThemeVariant;
        }
    }

    private void OnUiLanguageChanged(object? sender, EventArgs args)
    {
        if (_trayMainItem is { } main) main.Header = _uiText.Get("OpenMainWindow");
        if (_traySettingsItem is { } settings) settings.Header = _uiText.Get("Settings");
        if (_trayExitItem is { } exit) exit.Header = _uiText.Get("Exit");
    }

    private async Task RebindViewerAsync(
        WindowsClientRuntime runtime,
        CancellationToken cancellationToken)
    {
        var session = _viewerSession ?? throw new InvalidOperationException("Viewer session is unavailable.");
        var active = await runtime.Application.ActiveWiki.GetActiveAsync(cancellationToken);
        if (!active.IsPageDownloaded)
        {
            _shellViewModel.Reader.Detach();
            _shellViewModel.Reader.SetStatus(UiTextCatalog.Message("ThisWikiHasNoDownloadedPagesDownloadOr2"));
            _shellViewModel.CloseReader();
            return;
        }

        if (runtime.Viewer.Status.State != ViewerConsumerState.Ready)
            await runtime.Viewer.RetryAsync(cancellationToken);
        if (runtime.Viewer.Status is not { State: ViewerConsumerState.Ready, BaseUri: not null } ready)
            throw new InvalidOperationException("Viewer did not resume after the Wiki data operation.");

        _shellViewModel.Reader.SetTransportReady(false);
        _shellViewModel.SetWikiHomePage(active.DefaultPageTitle);
        session.Navigate(ready.BaseUri, active.Id, active.Title, forceReload: true);
        await session.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), cancellationToken);
        _shellViewModel.AttachReader(session.OpenPageAsync, session.BackAsync, session.HasHistory);
        if (session.CurrentPage is { } currentPage)
            await _shellViewModel.Reader.ApplyPageIdentityAsync(currentPage);
        _shellViewModel.SetStartupStatus(
            UiTextCatalog.Message("ReadyWebView", active.Title, runtime.WebView2.Version ?? "available"));
    }

    private async Task<WikiBookSnapshot> SwitchWikiAndRebindAsync(
        int wikiId,
        CancellationToken cancellationToken)
    {
        var runtime = _runtime ?? throw new InvalidOperationException("Wiki runtime is unavailable.");
        var active = await runtime.Application.ActiveWiki.GetActiveAsync(cancellationToken);
        if (active.Id == wikiId)
            return active;
        active = await runtime.Application.ActiveWiki.SwitchAsync(wikiId, cancellationToken);
        _shellViewModel.RefreshAfterWikiDataChange(clearHomePage: true);
        await RebindViewerAsync(runtime, cancellationToken);
        if (_quickWindow is { } quick) await quick.RebindAsync(resetContent: true);
        return active;
    }

    private void OnViewerPageCommitted(object? sender, ViewerPageIdentity identity)
    {
        if (sender is not ViewerWebViewSession session || !ReferenceEquals(session, _viewerSession))
            return;
        Dispatcher.UIThread.Post(async () =>
        {
            if (!ReferenceEquals(session, _viewerSession) || session.CurrentPage != identity)
                return;
            await _shellViewModel.Reader.ApplyPageIdentityAsync(identity);
            _shellViewModel.OnViewerPageCommitted(identity);
        });
    }

    private void OnViewerHistoryChanged(object? sender, bool hasHistory)
    {
        if (!ReferenceEquals(sender, _viewerSession))
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, _viewerSession))
                _shellViewModel.Reader.SetCanGoBack(hasHistory);
        });
    }

    internal static string CreateTemporaryExportPath(string targetPath)
    {
        var fullPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The export target requires a parent directory.", nameof(targetPath));
        return Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.exporting");
    }

    internal static void CompleteExportTarget(string temporaryPath, string targetPath)
    {
        if (!File.Exists(temporaryPath))
            throw new FileNotFoundException("The completed temporary export is missing.", temporaryPath);
        File.Move(temporaryPath, targetPath, overwrite: true);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return sanitized.Length == 0 ? "Terraria-Wiki" : sanitized;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private async Task WriteSmokeLogAsync()
    {
        if (_smokeLogPath is null)
            return;
        var result = (_smokeResult ?? WindowsSmokeResult.Failure("smoke-incomplete"))
            with { ExitCleanupCompleted = _cleanupComplete, Lifecycle = _timings };
        var fullPath = Path.GetFullPath(_smokeLogPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    internal static string? ParseOption(IReadOnlyList<string> arguments, string option)
    {
        string? value = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], option, StringComparison.Ordinal))
                continue;
            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                throw new ArgumentException($"{option} requires an absolute path.");
            if (value is not null)
                throw new ArgumentException($"{option} may only be supplied once.");
            if (!Path.IsPathFullyQualified(arguments[index]))
                throw new ArgumentException($"{option} requires an absolute path.");
            value = Path.GetFullPath(arguments[index]);
        }
        return value;
    }

    internal static string? ParseTextOption(IReadOnlyList<string> arguments, string option)
    {
        string? value = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], option, StringComparison.Ordinal))
                continue;
            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                throw new ArgumentException($"{option} requires a value.");
            if (value is not null)
                throw new ArgumentException($"{option} may only be supplied once.");
            value = arguments[index].Trim();
            if (AiTextLimits.IsTooLong(value))
                throw new ArgumentException($"{option} is too long.");
        }
        return value;
    }

    internal static bool HasFlag(IReadOnlyList<string> arguments, string option) =>
        arguments.Count(value => string.Equals(value, option, StringComparison.Ordinal)) switch
        {
            0 => false,
            1 => true,
            _ => throw new ArgumentException($"{option} may only be supplied once."),
        };

    private long ElapsedFromProcessStart() =>
        _processStartOffsetMilliseconds + _lifecycle.ElapsedMilliseconds;

    private static long GetProcessStartOffsetMilliseconds()
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

    internal static string[] WithDataRoot(IReadOnlyList<string> arguments, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot))
            throw new ArgumentException("An absolute data root is required.", nameof(dataRoot));

        var result = new List<string>(arguments.Count + 2);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "--data-root", StringComparison.Ordinal))
            {
                if (index + 1 < arguments.Count)
                    index++;
                continue;
            }
            if (argument.StartsWith("--data-root=", StringComparison.Ordinal))
                continue;
            result.Add(argument);
        }
        result.Add("--data-root");
        result.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)));
        return result.ToArray();
    }
}

internal sealed record WindowsSmokeResult(
    bool Success,
    string Message,
    int SearchResults,
    int ScriptRequests,
    int StyleRequests,
    int ImageRequests,
    int AudioRequests,
    int RemainingViewerHistory,
    bool ExitCleanupCompleted,
    string? Error,
    WindowsAiSmokeResult? Ai,
    WindowsUpdateSmokeResult? Update,
    WindowsLifecycleTimings? Lifecycle = null)
{
    public static WindowsSmokeResult Failure(
        string error,
        WindowsLifecycleTimings? lifecycle = null) => new(
        false,
        "Windows client smoke failed.",
        0,
        0,
        0,
        0,
        0,
        0,
        false,
        error,
        null,
        null,
        lifecycle);
}

internal sealed record WindowsUpdateSmokeResult(
    bool CanUpdate,
    bool IsPortable,
    string CurrentVersion,
    bool HasReleasesUri);

internal sealed class WindowsLifecycleTimings
{
    private readonly object _startupStageGate = new();

    [JsonPropertyName("startup_stages")]
    public List<WindowsStartupStageTiming> StartupStages { get; } = [];
    [JsonPropertyName("runtime_ready_ms")]
    public long RuntimeReadyMilliseconds { get; set; }
    [JsonPropertyName("viewer_server_ready_ms")]
    public long ViewerServerReadyMilliseconds { get; set; }
    [JsonPropertyName("viewer_ready_ms")]
    public long ViewerReadyMilliseconds { get; set; }
    [JsonPropertyName("viewer_navigation_completed_ms")]
    public long ViewerNavigationCompletedMilliseconds { get; set; }
    [JsonPropertyName("viewer_web_message_count")]
    public int ViewerWebMessageCount { get; set; }
    [JsonPropertyName("viewer_valid_message_count")]
    public int ViewerValidMessageCount { get; set; }
    [JsonPropertyName("viewer_last_request_method")]
    public string? ViewerLastRequestMethod { get; set; }
    [JsonPropertyName("viewer_last_completed_method")]
    public string? ViewerLastCompletedMethod { get; set; }
    [JsonPropertyName("viewer_last_bridge_error_code")]
    public string? ViewerLastBridgeErrorCode { get; set; }
    [JsonPropertyName("viewer_outer_request_count")]
    public int ViewerOuterRequestCount { get; set; }
    [JsonPropertyName("viewer_index_request_count")]
    public int ViewerIndexRequestCount { get; set; }
    [JsonPropertyName("viewer_common_script_request_count")]
    public int ViewerCommonScriptRequestCount { get; set; }
    [JsonPropertyName("viewer_app_script_request_count")]
    public int ViewerAppScriptRequestCount { get; set; }
    [JsonPropertyName("ai_ready_ms")]
    public long? AiReadyMilliseconds { get; set; }
    [JsonPropertyName("ai_start_completed_ms")]
    public long AiStartCompletedMilliseconds { get; set; }
    [JsonPropertyName("ai_dispatch_ms")]
    public long AiDispatchMilliseconds { get; set; }
    [JsonPropertyName("close_requested_ms")]
    public long CloseRequestedMilliseconds { get; set; }
    [JsonPropertyName("webview_stop_ms")]
    public long WebViewStopMilliseconds { get; set; }
    [JsonPropertyName("ai_stop_ms")]
    public long AiStopMilliseconds { get; set; }
    [JsonPropertyName("viewer_stop_ms")]
    public long ViewerStopMilliseconds { get; set; }
    [JsonPropertyName("database_close_ms")]
    public long DatabaseCloseMilliseconds { get; set; }
    [JsonPropertyName("runtime_dispose_ms")]
    public long RuntimeDisposeMilliseconds { get; set; }
    [JsonPropertyName("close_total_ms")]
    public long CloseTotalMilliseconds { get; set; }

    internal void RecordStartupStage(
        WindowsStartupStage stage,
        long elapsedMilliseconds,
        long durationMilliseconds)
    {
        lock (_startupStageGate)
        {
            StartupStages.Add(new WindowsStartupStageTiming(
                WindowsStartupClock.Name(stage),
                Math.Max(0, elapsedMilliseconds),
                Math.Max(0, durationMilliseconds)));
        }
    }
}

internal sealed record StartupFailurePresentation(
    StartupRecoveryKind Recovery,
    string Message);

internal sealed record WindowsAiSmokeResult(
    bool Success,
    bool CancellationVerified,
    string AnswerStatus,
    int AnswerDeltas,
    int ToolEvents,
    int Sources,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    long FirstDeltaMilliseconds,
    long TotalMilliseconds);
