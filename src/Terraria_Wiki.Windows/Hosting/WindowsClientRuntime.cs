using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.WikiTools;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Viewer;
using System.Diagnostics;

namespace Terraria_Wiki.Windows.Hosting;

internal sealed class WindowsClientRuntime : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly WindowsMountGuard _mountGuard;
    private readonly string[] _arguments;
    private readonly WikiToolConsumerCoordinator _wikiToolCoordinator;
    private readonly Action<ApplicationPreferences> _updateViewerPreferences;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _aiGate = new(1, 1);
    private AiRuntimeConfiguration? _aiConfiguration;
    private bool _aiConfigurationLoaded;
    private WindowsAiRuntime? _ai;
    private int _disposed;
    private int _answerReserved;

    private WindowsClientRuntime(
        string dataRoot,
        HttpClient httpClient,
        WindowsMountGuard mountGuard,
        WikiApplication application,
        HostSettingsStore hostSettings,
        ViewerConsumerCoordinator viewer,
        ViewerBridge bridge,
        ApplicationPreferences preferences,
        WebView2RuntimeStatus webView2,
        string[] arguments,
        WikiToolConsumerCoordinator wikiToolCoordinator,
        Action<ApplicationPreferences> updateViewerPreferences,
        WindowsAiRuntimeStatus aiStatus)
    {
        DataRoot = dataRoot;
        _httpClient = httpClient;
        _mountGuard = mountGuard;
        Application = application;
        HostSettings = hostSettings;
        Viewer = viewer;
        Bridge = bridge;
        Preferences = preferences;
        WebView2 = webView2;
        _arguments = arguments;
        _wikiToolCoordinator = wikiToolCoordinator;
        _updateViewerPreferences = updateViewerPreferences;
        AiStatus = aiStatus;
    }

    public string DataRoot { get; }
    public WikiApplication Application { get; }
    public HostSettingsStore HostSettings { get; }
    public ViewerConsumerCoordinator Viewer { get; }
    public ViewerBridge Bridge { get; }
    public ApplicationPreferences Preferences { get; private set; }
    public WebView2RuntimeStatus WebView2 { get; }
    public WindowsAiRuntime? Ai => _ai;
    public WindowsAiRuntimeStatus AiStatus { get; private set; }
    public WindowsRuntimeDisposeTimings DisposeTimings { get; } = new();
    public event EventHandler<WindowsAiRuntimeStatus>? AiStatusChanged;

    public static async Task<WindowsClientRuntime> CreateAsync(
        IReadOnlyList<string> arguments,
        Func<bool> isDarkTheme,
        Action<WindowsStartupStageMeasurement>? startupObserver = null,
        CancellationToken cancellationToken = default,
        HostSettingsStore? hostSettings = null)
    {
        var stageTimer = Stopwatch.StartNew();
        WebView2RuntimeStatus webView2;
        try
        {
            webView2 = WebView2RuntimeProbe.Probe();
        }
        finally
        {
            ReportStartupStage(startupObserver, WindowsStartupStage.WebView2Probe, stageTimer.ElapsedMilliseconds);
        }
        if (!webView2.Available)
            throw new WindowsClientStartupException("webview2-missing", webView2.Error ?? "WebView2 Runtime is unavailable.");

        var guard = new WindowsMountGuard();
        HttpClient? httpClient = null;
        ViewerConsumerCoordinator? viewer = null;
        var wikiToolCoordinator = new WikiToolConsumerCoordinator();
        WikiApplication? application = null;
        ApplicationPreferences? loadedPreferences = null;
        try
        {
            DataRootResolution resolution;
            HostSettingsStore settings;
            stageTimer.Restart();
            try
            {
                guard.AcquireSingleInstance();
                var settingsPath = ParseHostSettingsPath(arguments);
                settings = hostSettings ?? (settingsPath is null ? HostSettingsStore.CreateDefault() : new HostSettingsStore(settingsPath));
                resolution = await new DataRootResolver(settings)
                    .ResolveAsync(arguments, cancellationToken)
                    .ConfigureAwait(false);
                guard.AcquireDataRoot(resolution.Path);
            }
            finally
            {
                ReportStartupStage(startupObserver, WindowsStartupStage.DataRootAndLocks, stageTimer.ElapsedMilliseconds);
            }

            httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var dataSource = new ApplicationViewerDataSource(() =>
                application ?? throw new InvalidOperationException("WikiApplication has not been created."));
            var staticRoot = Path.Combine(AppContext.BaseDirectory, "ViewerAssets");
            viewer = new ViewerConsumerCoordinator(
                staticRoot,
                dataSource,
                () => loadedPreferences?.ContentTheme == "original"
                    ? "original"
                    : isDarkTheme() ? "dark" : "light");
            var consumers = new CompositeWikiDataConsumerCoordinator(viewer, wikiToolCoordinator);
            application = new WikiApplication(
                resolution.Path,
                settings,
                consumers,
                new HttpClientWikiHttpTransport(httpClient),
                searchAliasPath: Path.Combine(
                    AppContext.BaseDirectory,
                    "SearchAssets",
                    "community-aliases.zh-cn.json"));
            viewer.SetActiveWikiProvider(application.ActiveWiki.GetActiveAsync);

            await application.ActiveWiki.InitializeAsync(
                    timing => ReportStartupStage(
                        startupObserver,
                        MapStartupStage(timing.Stage),
                        timing.DurationMilliseconds),
                    cancellationToken)
                .ConfigureAwait(false);
            stageTimer.Restart();
            ApplicationPreferences preferences;
            try
            {
                preferences = await application.Settings.GetAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ReportStartupStage(startupObserver, WindowsStartupStage.RuntimePreferences, stageTimer.ElapsedMilliseconds);
            }
            loadedPreferences = preferences;
            var bridge = new ViewerBridge(
                application,
                new WindowsExternalUriLauncher(),
                preferences.IsSideButtonBackEnabled);

            return new WindowsClientRuntime(
                resolution.Path,
                httpClient,
                guard,
                application,
                settings,
                viewer,
                bridge,
                preferences,
                webView2,
                arguments.ToArray(),
                wikiToolCoordinator,
                value => loadedPreferences = value,
                new WindowsAiRuntimeStatus(WindowsAiRuntimeState.Starting, "AI 配置等待后台加载。"));
        }
        catch
        {
            if (viewer is not null)
            {
                try { await viewer.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            if (application is not null)
            {
                try { await application.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            httpClient?.Dispose();
            guard.Dispose();
            throw;
        }
    }

    public void ApplyPreferences(ApplicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Preferences = preferences;
        _updateViewerPreferences(preferences);
        Bridge.SetSideButtonBackEnabled(preferences.IsSideButtonBackEnabled);
    }

    internal bool TryReserveAiAnswer()
    {
        if (_ai is null || AiStatus.State != WindowsAiRuntimeState.Ready
            || Interlocked.CompareExchange(ref _answerReserved, 1, 0) != 0)
            return false;
        if (_ai.Status.State != WindowsAiRuntimeState.Ready)
        {
            Interlocked.Exchange(ref _answerReserved, 0);
            return false;
        }
        PublishAiStatus(new WindowsAiRuntimeStatus(WindowsAiRuntimeState.Answering,
            "AI 正在另一个窗口回答…"));
        return true;
    }

    internal void ReleaseAiAnswer()
    {
        if (Interlocked.Exchange(ref _answerReserved, 0) != 0 && _ai is { } ai)
            PublishAiStatus(ai.Status);
    }

    public async Task EnableAiAsync(CancellationToken cancellationToken = default)
    {
        using var attempt = WindowsStartupInvestigation.BeginAiAttempt();
        using var attemptTiming = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AiAttempt);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AiGateWait))
            await _aiGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            await EnableAiUnderLockAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _aiGate.Release();
        }
    }

    private async Task EnableAiUnderLockAsync(CancellationToken cancellationToken)
    {
        if (_ai is not null)
        {
            PublishAiStatus(AiStatus);
            return;
        }
        if (!_aiConfigurationLoaded)
        {
            _aiConfigurationLoaded = true;
            try
            {
                using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AiConfiguration))
                    _aiConfiguration = await AiRuntimeConfiguration.LoadAsync(_arguments, cancellationToken)
                        .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(_aiConfiguration.ApiKey))
                {
                    PublishAiStatus(new WindowsAiRuntimeStatus(
                        WindowsAiRuntimeState.Disabled,
                        "未配置 AI API Key；离线搜索和阅读仍可使用。",
                        "model_authentication"));
                    return;
                }
            }
            catch (AiConfigurationException exception)
            {
                _aiConfiguration = null;
                PublishAiStatus(new WindowsAiRuntimeStatus(
                    WindowsAiRuntimeState.Disabled,
                    exception.Message + " 离线搜索和阅读仍可使用。",
                    exception.Code));
                return;
            }
        }
        if (_aiConfiguration is null || AiStatus.State == WindowsAiRuntimeState.Disabled)
        {
            PublishAiStatus(AiStatus);
            return;
        }
        PublishAiStatus(new WindowsAiRuntimeStatus(WindowsAiRuntimeState.Starting, "正在启动 AI Runtime…"));
        try
        {
            var ai = await WindowsAiRuntime.StartAsync(
                Application.Query,
                _wikiToolCoordinator,
                _aiConfiguration,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _disposed) != 0 || _lifetime.IsCancellationRequested)
            {
                await ai.DisposeAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            _ai = ai;
            ai.StatusChanged += OnAiStatusChanged;
            PublishAiStatus(ai.Status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiRuntimeException exception)
        {
            PublishAiStatus(new WindowsAiRuntimeStatus(
                WindowsAiRuntimeState.Failed,
                exception.Message + " 离线搜索和阅读仍可使用。",
                exception.Code));
        }
        catch
        {
            PublishAiStatus(new WindowsAiRuntimeStatus(
                WindowsAiRuntimeState.Failed,
                "AI Runtime 启动失败；离线搜索和阅读仍可使用。",
                "unknown"));
        }
    }

    public async Task RetryAiAsync(CancellationToken cancellationToken = default)
    {
        using var attempt = WindowsStartupInvestigation.BeginAiAttempt();
        using var attemptTiming = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AiAttempt);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AiGateWait))
            await _aiGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var old = _ai;
            _ai = null;
            if (old is not null)
            {
                old.StatusChanged -= OnAiStatusChanged;
                using var disposeTiming = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.AiPreviousRuntimeDispose);
                try { await old.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            _aiConfiguration = null;
            _aiConfigurationLoaded = false;
            PublishAiStatus(new WindowsAiRuntimeStatus(WindowsAiRuntimeState.Starting, "AI Runtime 等待启动。"));
            await EnableAiUnderLockAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _aiGate.Release();
        }
    }

    public ValueTask DisposeAsync() => DisposeAsync(Task.CompletedTask);

    internal async ValueTask DisposeAsync(Task viewerSessionStop)
    {
        ArgumentNullException.ThrowIfNull(viewerSessionStop);
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        var total = Stopwatch.StartNew();
        _lifetime.Cancel();
        try
        {
            var aiStop = StopAiAsync();
            var viewerStop = StopViewerAfterAsync(viewerSessionStop);
            await Task.WhenAll(aiStop, viewerStop).ConfigureAwait(false);
        }
        finally
        {
            var application = Stopwatch.StartNew();
            try
            {
                await Application.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                application.Stop();
                DisposeTimings.ApplicationStopMilliseconds = application.ElapsedMilliseconds;
                _httpClient.Dispose();
                _mountGuard.Dispose();
                _aiGate.Dispose();
                _lifetime.Dispose();
                total.Stop();
                DisposeTimings.TotalMilliseconds = total.ElapsedMilliseconds;
            }
        }
    }

    private async Task StopViewerAfterAsync(Task viewerSessionStop)
    {
        try { await viewerSessionStop.ConfigureAwait(false); } catch { }
        await StopViewerAsync().ConfigureAwait(false);
    }

    private async Task StopAiAsync()
    {
        var timer = Stopwatch.StartNew();
        await _aiGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var ai = _ai;
            _ai = null;
            if (ai is not null)
            {
                ai.StatusChanged -= OnAiStatusChanged;
                try { await ai.DisposeAsync().ConfigureAwait(false); } catch { }
            }
        }
        finally
        {
            _aiGate.Release();
            timer.Stop();
            DisposeTimings.AiStopMilliseconds = timer.ElapsedMilliseconds;
        }
    }

    private async Task StopViewerAsync()
    {
        var timer = Stopwatch.StartNew();
        try { await Viewer.StopAsync().ConfigureAwait(false); } catch { }
        finally
        {
            timer.Stop();
            DisposeTimings.ViewerStopMilliseconds = timer.ElapsedMilliseconds;
        }
    }

    private void OnAiStatusChanged(object? sender, WindowsAiRuntimeStatus status) => PublishAiStatus(status);

    private void PublishAiStatus(WindowsAiRuntimeStatus status)
    {
        AiStatus = status;
        var phase = status.State switch
        {
            WindowsAiRuntimeState.Starting => StartupInvestigationPhase.AiStarting,
            WindowsAiRuntimeState.Ready => StartupInvestigationPhase.AiReady,
            WindowsAiRuntimeState.Disabled => StartupInvestigationPhase.AiDisabled,
            WindowsAiRuntimeState.Failed => StartupInvestigationPhase.AiFailed,
            _ => StartupInvestigationPhase.AiStopped,
        };
        if (status.State != WindowsAiRuntimeState.Answering)
            WindowsStartupInvestigation.Checkpoint(phase);
        try { AiStatusChanged?.Invoke(this, status); } catch { }
    }

    internal static string? ParseHostSettingsPath(IReadOnlyList<string> arguments)
    {
        string? value = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], "--host-settings", StringComparison.Ordinal))
                continue;
            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                throw new WindowsClientStartupException("missing-host-settings-argument", "--host-settings requires an absolute file path.");
            if (value is not null)
                throw new WindowsClientStartupException("duplicate-host-settings-argument", "--host-settings may only be supplied once.");
            if (!Path.IsPathFullyQualified(arguments[index]))
                throw new WindowsClientStartupException("invalid-host-settings-argument", "--host-settings must be an absolute file path.");
            value = Path.GetFullPath(arguments[index]);
        }
        return value;
    }

    private static WindowsStartupStage MapStartupStage(ApplicationStartupStage stage) => stage switch
    {
        ApplicationStartupStage.DataLayoutPreflight => WindowsStartupStage.DataLayoutPreflight,
        ApplicationStartupStage.ManagerInitializationAndRecovery => WindowsStartupStage.ManagerInitializationAndRecovery,
        ApplicationStartupStage.Settings => WindowsStartupStage.Settings,
        ApplicationStartupStage.CompatibilityPreparation => WindowsStartupStage.CompatibilityPreparation,
        ApplicationStartupStage.ContentInitialization => WindowsStartupStage.ContentInitialization,
        ApplicationStartupStage.StatisticsSnapshot => WindowsStartupStage.StatisticsSnapshot,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    private static void ReportStartupStage(
        Action<WindowsStartupStageMeasurement>? observer,
        WindowsStartupStage stage,
        long durationMilliseconds)
    {
        if (observer is null)
            return;
        try { observer(new WindowsStartupStageMeasurement(stage, durationMilliseconds)); }
        catch { }
    }
}

internal sealed class WindowsRuntimeDisposeTimings
{
    public long AiStopMilliseconds { get; internal set; }
    public long ViewerStopMilliseconds { get; internal set; }
    public long ApplicationStopMilliseconds { get; internal set; }
    public long TotalMilliseconds { get; internal set; }
}

internal sealed class WindowsClientStartupException : IOException
{
    public WindowsClientStartupException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
