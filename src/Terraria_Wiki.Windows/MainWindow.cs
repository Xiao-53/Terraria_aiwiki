using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Favorites;
using Terraria_Wiki.Windows.Presentation.Management;
using Terraria_Wiki.Windows.Presentation.Shell;
using Terraria_Wiki.Windows.Updates;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows;

internal sealed partial class MainWindow : Window
{
    private static readonly Uri WebView2DownloadUri = new(
        "https://developer.microsoft.com/microsoft-edge/webview2/",
        UriKind.Absolute);

    private readonly string[] _arguments;
    private string[] _runtimeArguments;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _smokeLogPath;
    private readonly string? _chatFavoritesPath;
    private readonly string? _diagnosticsDirectory;
    private readonly string? _aiSmokeQuestion;
    private readonly bool _aiSmokeCancelFirst;
    private readonly Stopwatch _lifecycle = Stopwatch.StartNew();
    private readonly WindowsLifecycleTimings _timings = new();
    private readonly long _processStartOffsetMilliseconds;
    private readonly ShellViewModel _shellViewModel;
    private readonly ShellView _shellView;
    private readonly IProductUpdateService _productUpdateService;
    private readonly ProductDiagnostics _diagnostics;
    private WindowsClientRuntime? _runtime;
    private ViewerWebViewSession? _viewerSession;
    private bool _cleanupComplete;
    private bool _closing;
    private bool _isRestarting;
    private WindowsSmokeResult? _smokeResult;
    private Task _aiStartupTask = Task.CompletedTask;

    public MainWindow(string[] arguments)
    {
        _arguments = arguments ?? [];
        _runtimeArguments = _arguments.ToArray();
        _smokeLogPath = ParseOption(_arguments, "--smoke-log");
        _chatFavoritesPath = ParseOption(_arguments, "--chat-favorites");
        _diagnosticsDirectory = ParseOption(_arguments, "--diagnostics-directory");
        _aiSmokeQuestion = ParseTextOption(_arguments, "--ai-smoke-question");
        _aiSmokeCancelFirst = HasFlag(_arguments, "--ai-smoke-cancel-first");
        _processStartOffsetMilliseconds = GetProcessStartOffsetMilliseconds();

        AvaloniaXamlLoader.Load(this);
        _shellView = this.FindControl<ShellView>("ShellHost")!;
        _productUpdateService = new VelopackProductUpdateService();
        _diagnostics = _diagnosticsDirectory is null
            ? ProductDiagnostics.CreateDefault()
            : new ProductDiagnostics(_diagnosticsDirectory);
        _diagnostics.Write(ProductDiagnosticEvent.ApplicationStarted);
        _shellViewModel = new ShellViewModel(
            CreateChatFavoriteStore(_chatFavoritesPath),
            PickImportPackageAsync,
            ExportPackageAsync,
            ApplyPreferencesAsync,
            LoadAiConfigurationDraftAsync,
            SaveAiConfigurationAsync,
            RestartAiAsync,
            ClearAiCredentialAsync,
            _productUpdateService,
            ApplyUpdateAndRestartAsync,
            OpenExternalUriAsync,
            _diagnostics);
        _shellView.DataContext = _shellViewModel;
        _shellViewModel.RetryStartupRequested += OnRetryStartupRequested;
        _shellViewModel.SelectDataRootRequested += OnSelectDataRootRequested;
        _shellViewModel.GetWebView2Requested += OnGetWebView2Requested;
        _shellViewModel.Management.DataChanged += OnWikiDataChanged;
        _shellView.WebView.NavigationCompleted += OnViewerNavigationCompleted;

        Opened += OnOpened;
        Closing += OnClosing;
    }

    private static IChatFavoriteStore CreateChatFavoriteStore(string? filePath)
    {
        try
        {
            return filePath is null
                ? ChatFavoriteStore.CreateDefault()
                : new ChatFavoriteStore(filePath);
        }
        catch
        {
            return new MemoryChatFavoriteStore();
        }
    }

    private async void OnOpened(object? sender, EventArgs args) => await StartAsync();

    private async void OnRetryStartupRequested(object? sender, EventArgs args) => await RetryStartupAsync();

    private async void OnSelectDataRootRequested(object? sender, EventArgs args) => await SelectDataRootAsync();

    private async void OnGetWebView2Requested(object? sender, EventArgs args) =>
        await OpenWebView2DownloadAsync();

    private async Task OpenWebView2DownloadAsync()
    {
        _diagnostics.Write(ProductDiagnosticEvent.WebView2RecoveryOpened);
        await OpenExternalUriAsync(WebView2DownloadUri);
    }

    private async void OnWikiDataChanged(object? sender, WikiDataChangedEventArgs args)
    {
        _shellViewModel.RefreshAfterWikiDataChange();
        if (!args.RequiresViewerRebind || _runtime is null)
            return;
        try
        {
            await RebindViewerAsync(_runtime, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            _shellViewModel.SetStartupStatus(
                "Wiki 数据已改变，但 Reader 暂时无法恢复；离线数据管理仍可使用。",
                isError: true);
        }
    }

    private async Task StartAsync()
    {
        _shellViewModel.SetStartupStatus("正在确认 WebView2、Wiki 数据目录和本地数据库…");
        try
        {
            var runtime = await WindowsClientRuntime.CreateAsync(
                _runtimeArguments,
                IsDarkTheme,
                _lifetime.Token);
            _runtime = runtime;
            _shellViewModel.SetWebView2Status(runtime.WebView2);
            _diagnostics.Write(ProductDiagnosticEvent.RuntimeReady, runtime.WebView2.Version);
            _timings.RuntimeReadyMilliseconds = ElapsedFromProcessStart();
            runtime.Viewer.StatusChanged += OnViewerStatusChanged;
            RequestedThemeVariant = runtime.Preferences.AppTheme switch
            {
                "dark" => ThemeVariant.Dark,
                "light" => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };

            await runtime.Viewer.EnableAsync(_lifetime.Token);
            _timings.ViewerServerReadyMilliseconds = ElapsedFromProcessStart();

            var session = new ViewerWebViewSession(_shellView.WebView, runtime.Bridge);
            _viewerSession = session;
            session.StatusChanged += OnViewerSessionStatusChanged;
            session.ProtocolError += OnViewerProtocolError;
            session.ReadyChanged += OnViewerReadyChanged;

            if (runtime.Viewer.Status is not { State: ViewerConsumerState.Ready, BaseUri: not null } ready)
                throw new WindowsClientStartupException(
                    "viewer-not-ready",
                    runtime.Viewer.Status.Message ?? "Viewer is not ready.");

            _shellViewModel.AttachRuntime(runtime);
            var activeWiki = await runtime.Application.ActiveWiki.GetActiveAsync(_lifetime.Token);
            _aiStartupTask = EnableAiInBackgroundAsync(runtime);
            if (!activeWiki.IsPageDownloaded)
            {
                _shellViewModel.Reader.Detach();
                _shellViewModel.Reader.SetStatus("当前 Wiki 尚未下载页面；请先在数据管理中下载或导入。");
                _shellViewModel.CloseReader();
                _shellViewModel.SetStartupStatus("本地 Runtime 已就绪 · 当前 Wiki 尚未下载数据");
                return;
            }

            _shellViewModel.Reader.SetStatus("正在载入本地 Wiki 首页…");
            session.Navigate(ready.BaseUri);

            await session.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), _lifetime.Token);
            _timings.ViewerReadyMilliseconds = ElapsedFromProcessStart();
            await _shellViewModel.Reader.SetHomePageAsync(activeWiki.DefaultPageTitle);
            _shellViewModel.AttachReader(session.OpenPageAsync, session.BackAsync);
            _shellViewModel.SetStartupStatus(
                $"本地 Wiki 已就绪 · WebView2 {runtime.WebView2.Version ?? "available"}");

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
            await DisposeRuntimeAsync();
            ShowStartupError(exception);
            if (_smokeLogPath is not null)
            {
                _smokeResult = WindowsSmokeResult.Failure(GetSafeErrorCode(exception), _timings);
                Close();
            }
        }
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

    private async Task SelectDataRootAsync()
    {
        if (_closing || _isRestarting)
            return;
        _isRestarting = true;
        _shellViewModel.Management.BeginDataRootSelection();
        try
        {
            var selected = await new AvaloniaDataRootPicker(this).PickAsync(_lifetime.Token);
            if (string.IsNullOrWhiteSpace(selected))
            {
                _shellViewModel.Management.ReportDataRootSelection(
                    "已取消选择；当前数据文件夹没有改变，可以随时重新选择。");
                return;
            }
            if (!DataRootResolver.TryPrepareForInitialization(selected, out var normalized, out _))
            {
                _shellViewModel.Management.ReportDataRootSelection(
                    "所选文件夹不可用；请选择空文件夹或包含有效 Manager.db 的 Wiki 数据文件夹。你可以立即重新选择。");
                return;
            }

            _runtimeArguments = WithDataRoot(_arguments, normalized!);
            _shellViewModel.Management.SetDataRoot(normalized);
            _shellViewModel.Management.ReportDataRootSelection("正在切换 Wiki 数据文件夹…");
            _shellViewModel.SetStartupStatus("正在切换 Wiki 数据文件夹…");
            await DisposeRuntimeAsync();
            await StartAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch
        {
            _shellViewModel.Management.ReportDataRootSelection(
                "无法打开或读取所选文件夹；当前数据没有改变，可以重新选择。");
        }
        finally
        {
            _shellViewModel.Management.EndDataRootSelection();
            _isRestarting = false;
        }
    }

    private bool IsDarkTheme() =>
        RequestedThemeVariant == ThemeVariant.Dark
        || (RequestedThemeVariant == ThemeVariant.Default && ActualThemeVariant == ThemeVariant.Dark);

    private void OnViewerNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs args)
    {
        _timings.ViewerNavigationCompletedMilliseconds = ElapsedFromProcessStart();
        _shellViewModel.Reader.SetStatus("正在准备本地 Wiki 内容…");
    }

    private void OnViewerSessionStatusChanged(object? sender, string message) =>
        Dispatcher.UIThread.Post(() => _shellViewModel.Reader.SetStatus(message));

    private void OnViewerProtocolError(object? sender, ViewerProtocolError error) =>
        Dispatcher.UIThread.Post(() => _shellViewModel.Reader.SetStatus($"Viewer：{error.Message}"));

    private void OnViewerReadyChanged(object? sender, bool isReady) =>
        Dispatcher.UIThread.Post(() => _shellViewModel.Reader.SetTransportReady(isReady));

    private void OnViewerStatusChanged(object? sender, ViewerConsumerStatus status) =>
        Dispatcher.UIThread.Post(() =>
        {
            _shellViewModel.Reader.SetStatus(
                status.Error is null
                    ? status.Message ?? status.State.ToString()
                    : "Viewer 暂时不可用。");
            if (status.State == ViewerConsumerState.Failed)
                _shellViewModel.SetStartupStatus("Viewer 启动失败；处理后重试。", isError: true);
        });

    private void ShowStartupError(Exception exception)
    {
        var recovery = exception switch
        {
            WindowsClientStartupException { Code: "webview2-missing" } => StartupRecoveryKind.InstallWebView2,
            DataRootResolutionException => StartupRecoveryKind.SelectDataRoot,
            ApplicationUseCaseException { Code: "initialization-failed" or "no-wikis" } =>
                StartupRecoveryKind.SelectDataRoot,
            _ => StartupRecoveryKind.Retry,
        };
        var message = exception switch
        {
            HostMountException mount => mount.Message,
            DataRootResolutionException dataRoot => dataRoot.Message,
            HostSettingsException settings => settings.Message,
            WindowsClientStartupException { Code: "webview2-missing" } =>
                "未检测到 Microsoft Edge WebView2 Runtime；请从 Microsoft 官方页面安装或修复 Evergreen Runtime。",
            WindowsClientStartupException startup => startup.Message,
            ApplicationUseCaseException application => application.Message,
            _ => "应用启动失败。",
        };
        _diagnostics.Write(ProductDiagnosticEvent.StartupFailed, GetSafeErrorCode(exception));
        if (recovery == StartupRecoveryKind.SelectDataRoot)
        {
            var dataRootMessage = exception switch
            {
                DataRootResolutionException { Code: "data-root-required" } =>
                    "已保存的 Wiki 数据文件夹不可用。请在数据管理页面更改到空文件夹或包含有效 Manager.db 的文件夹。",
                DataRootResolutionException { Code: "default-data-root-unavailable" } =>
                    "无法创建默认 Wiki 数据文件夹。请在数据管理页面选择其他空文件夹或已有数据库文件夹。",
                _ =>
                    "Wiki 数据库无法初始化。请在数据管理页面更改到空文件夹或包含有效 Manager.db 的文件夹。",
            };
            _shellViewModel.ShowDataRootRecovery(dataRootMessage);
            return;
        }
        _shellViewModel.SetStartupStatus(
            message + " 处理后点击“重试”。",
            isError: true,
            recovery);
    }

    private static string GetSafeErrorCode(Exception exception) => exception switch
    {
        WindowsClientStartupException startup => startup.Code,
        DataRootResolutionException dataRoot => dataRoot.Code,
        HostSettingsException settings => settings.Code,
        HostMountException mount => mount.Code,
        ApplicationUseCaseException application => application.Code,
        _ => exception.GetType().Name,
    };

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

            await session.OpenPageAsync("Smoke Alias", _lifetime.Token);
            await session.ChangeThemeAsync(isDark: true, _lifetime.Token);
            await session.BackAsync(_lifetime.Token);
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
            _shellViewModel.SetStartupStatus("Smoke 失败。", isError: true);
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
        _closing = true;
        await CleanupAsync();
        Close();
    }

    private async Task ApplyUpdateAndRestartAsync()
    {
        if (_closing)
            return;
        _closing = true;
        await CleanupAsync();
        _productUpdateService.ApplyAndRestart();
        Close();
    }

    private static Task OpenExternalUriAsync(Uri uri) =>
        new WindowsExternalUriLauncher().OpenAsync(uri);

    private async Task CleanupAsync()
    {
        var closeTimer = Stopwatch.StartNew();
        _timings.CloseRequestedMilliseconds = ElapsedFromProcessStart();
        _lifetime.Cancel();
        _shellViewModel.SetStartupStatus("正在关闭 AI、Viewer 和本地数据库…");
        await DisposeRuntimeAsync();
        _shellViewModel.Dispose();
        _shellViewModel.RetryStartupRequested -= OnRetryStartupRequested;
        _shellViewModel.SelectDataRootRequested -= OnSelectDataRootRequested;
        _shellViewModel.GetWebView2Requested -= OnGetWebView2Requested;
        _shellViewModel.Management.DataChanged -= OnWikiDataChanged;
        _shellView.WebView.NavigationCompleted -= OnViewerNavigationCompleted;
        _lifetime.Dispose();
        _diagnostics.Write(ProductDiagnosticEvent.ApplicationStopped);
        _cleanupComplete = true;
        closeTimer.Stop();
        _timings.CloseTotalMilliseconds = closeTimer.ElapsedMilliseconds;
        await WriteSmokeLogAsync();
    }

    private async Task DisposeRuntimeAsync()
    {
        _shellViewModel.DetachRuntime();

        var session = _viewerSession;
        _viewerSession = null;
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
            var sessionTimer = Stopwatch.StartNew();
            try { await session.DisposeAsync(); } catch { }
            sessionTimer.Stop();
            if (_closing)
                _timings.WebViewStopMilliseconds = sessionTimer.ElapsedMilliseconds;
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
            try { await runtime.DisposeAsync(); } catch { }
            if (_closing)
            {
                _timings.AiStopMilliseconds = runtime.DisposeTimings.AiStopMilliseconds;
                _timings.ViewerStopMilliseconds = runtime.DisposeTimings.ViewerStopMilliseconds;
                _timings.DatabaseCloseMilliseconds = runtime.DisposeTimings.ApplicationStopMilliseconds;
                _timings.RuntimeDisposeMilliseconds = runtime.DisposeTimings.TotalMilliseconds;
            }
        }
    }

    private async Task EnableAiInBackgroundAsync(WindowsClientRuntime runtime)
    {
        try
        {
            await runtime.EnableAiAsync(_lifetime.Token);
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

    private async Task<string?> PickImportPackageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Wiki 数据包",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Wiki 数据包") { Patterns = ["*.pkg"] },
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
                "AI 配置已保存；选择有效的 Wiki 数据文件夹后即可启动 AI。",
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

    private AiRuntimeConfigurationStore CreateAiConfigurationStore()
    {
        var path = AiRuntimeConfiguration.ParseConfigurationPath(_runtimeArguments)
            ?? AiRuntimeConfiguration.DefaultConfigurationPath();
        return new AiRuntimeConfigurationStore(path);
    }

    private async Task<WikiPackageExportResponse?> ExportPackageAsync(
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
            Title = "导出 Wiki 数据包",
            SuggestedFileName = SanitizeFileName(wiki.Title) + "-" + DateTime.Now.ToString("yyyyMMdd") + ".pkg",
            DefaultExtension = "pkg",
            FileTypeChoices =
            [
                new FilePickerFileType("Wiki 数据包") { Patterns = ["*.pkg"] },
            ],
        });
        cancellationToken.ThrowIfCancellationRequested();
        if (file?.Path is not { IsFile: true } path)
            return null;

        var targetPath = Path.GetFullPath(path.LocalPath);
        var exportPath = File.Exists(targetPath) ? CreateTemporaryExportPath(targetPath) : targetPath;
        try
        {
            var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var result = await runtime.Application.Data.ExportPackageAsync(
                wiki.Id,
                exportPath,
                version,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(exportPath, targetPath, StringComparison.OrdinalIgnoreCase))
                CompleteExportTarget(exportPath, targetPath);
            return result with { PackagePath = targetPath };
        }
        finally
        {
            if (!string.Equals(exportPath, targetPath, StringComparison.OrdinalIgnoreCase))
                TryDeleteFile(exportPath);
        }
    }

    private async Task ApplyPreferencesAsync(ApplicationPreferences preferences)
    {
        var runtime = _runtime;
        if (runtime is null)
            return;
        var previous = runtime.Preferences;
        runtime.ApplyPreferences(preferences);
        RequestedThemeVariant = preferences.AppTheme switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };

        if (!string.Equals(previous.ContentTheme, preferences.ContentTheme, StringComparison.Ordinal))
        {
            await runtime.Viewer.RetryAsync(_lifetime.Token);
            await RebindViewerAsync(runtime, _lifetime.Token);
        }
        else if (!string.Equals(previous.AppTheme, preferences.AppTheme, StringComparison.Ordinal)
                 && preferences.ContentTheme == "auto"
                 && _viewerSession is { IsReady: true } session)
        {
            await session.ChangeThemeAsync(IsDarkTheme(), _lifetime.Token);
        }
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
            _shellViewModel.Reader.SetStatus("当前 Wiki 尚未下载页面；请先在数据管理中下载或导入。");
            _shellViewModel.CloseReader();
            return;
        }

        if (runtime.Viewer.Status.State != ViewerConsumerState.Ready)
            await runtime.Viewer.RetryAsync(cancellationToken);
        if (runtime.Viewer.Status is not { State: ViewerConsumerState.Ready, BaseUri: not null } ready)
            throw new InvalidOperationException("Viewer did not resume after the Wiki data operation.");

        _shellViewModel.Reader.SetTransportReady(false);
        session.Navigate(ready.BaseUri, forceReload: true);
        await session.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), cancellationToken);
        await _shellViewModel.Reader.SetHomePageAsync(active.DefaultPageTitle);
        _shellViewModel.AttachReader(session.OpenPageAsync, session.BackAsync);
        _shellViewModel.SetStartupStatus(
            $"{active.Title} 已就绪 · WebView2 {runtime.WebView2.Version ?? "available"}");
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
            if (value.Length > 32_768)
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
}

internal sealed record WindowsAiSmokeResult(
    bool Success,
    bool CancellationVerified,
    string AnswerStatus,
    int AnswerDeltas,
    int ToolEvents,
    int Sources,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    long FirstDeltaMilliseconds,
    long TotalMilliseconds);
