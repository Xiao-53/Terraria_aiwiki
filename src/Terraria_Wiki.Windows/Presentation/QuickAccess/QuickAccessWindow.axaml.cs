using System.Diagnostics;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Chat;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Reader;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows.Presentation.QuickAccess;

internal sealed partial class QuickAccessWindow : Window
{
    private readonly NativeWebView _webView;
    private readonly TextBox _questionBox;
    private readonly TextBox _searchBox;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ProductDiagnostics _diagnostics;
    private readonly Stopwatch _firstRequestClock;
    private readonly QuickAccessFocusSession _focus;
    private ViewerWebViewSession? _session;
    private ViewerBridge? _bridge;
    private CancellationTokenSource? _rebindCancellation;
    private WindowsClientRuntime? _runtime;
    private int _activeWikiId;
    private long _generation;
    private string? _pendingTitle;
    private string? _currentTitle;
    private string? _wikiTitle;
    private string? _sourceTitle;
    private bool _exiting;
    private bool _firstPageCommitted;
    private bool _firstWindowShown;
    private bool _firstAdapterCreated;
    private bool _firstDomReady;

    public QuickAccessWindow(IChatConversationStore store,
        ProductDiagnostics diagnostics, Stopwatch firstRequestClock, UiTextCatalog? uiText = null)
    {
        _diagnostics = diagnostics;
        _firstRequestClock = firstRequestClock;
        AvaloniaXamlLoader.Load(this);
        uiText ??= new UiTextCatalog();
        uiText.Attach(Resources);
        _focus = new QuickAccessFocusSession(QuickAccessNativeFocus.Foreground,
            QuickAccessNativeFocus.Inspect,
            foreground => QuickAccessNativeFocus.BelongsTo(foreground, NativeHandle),
            QuickAccessNativeFocus.Restore);
        Deactivated += (_, _) => _focus.InvalidateFocus();
        ViewModel = new QuickAccessViewModel(store, OpenArticleAsync, RestoreSelectedPageAsync,
            () => _ = RebindAsync()).WithText(uiText);
        DataContext = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _webView = this.FindControl<NativeWebView>("ViewerWebView")!;
        _webView.EnvironmentRequested += OnWebViewEnvironmentRequested;
        _webView.AdapterCreated += OnAdapterCreated;
        _questionBox = this.FindControl<TextBox>("QuestionBox")!;
        _searchBox = this.FindControl<TextBox>("SearchBox")!;
        _questionBox.AddHandler(InputElement.KeyDownEvent, OnQuestionKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _searchBox.KeyDown += OnSearchKeyDown;
        UpdateViewerSize();
        KeyDown += OnWindowKeyDown;
        Closing += OnClosing;
    }

    public QuickAccessViewModel ViewModel { get; }
    public event EventHandler? HiddenByUser;
    public void MarkHostUnavailable() =>
        ViewModel.SetViewerStatus(UiTextCatalog.Message("TheMainWindowIsNotReadyResolveIts"), failed: true);

    private nint NativeHandle => TryGetPlatformHandle()?.Handle ?? 0;

    public void ApplyTopmostPreference(bool value)
    {
        _focus.ApplyTopmostPreference(value);
        Topmost = _focus.EffectiveTopmost;
    }

    public void ShowQuick(QuickAccessPreferences preferences, QuickAccessShowIntent intent)
    {
        var activate = _focus.Show(intent, preferences.Topmost);
        var wasVisible = IsVisible;
        try
        {
            ShowActivated = activate;
            Topmost = _focus.EffectiveTopmost;
            if (!IsVisible)
            {
                ViewModel.SelectDefault(preferences.DefaultPage);
                UpdateViewerSize();
                Show();
                if (!_firstWindowShown)
                {
                    _firstWindowShown = true;
                    _diagnostics.WriteQuickAccessStage(QuickAccessStage.FirstWindowShown,
                        _firstRequestClock.ElapsedMilliseconds);
                }
            }
            if (activate)
            {
                Activate();
                FocusInput();
            }
            else QuickAccessNativeFocus.RaiseWithoutActivation(NativeHandle);
        }
        catch
        {
            if (!wasVisible)
            {
                _focus.Clear();
                Hide();
                Topmost = _focus.EffectiveTopmost;
            }
            throw;
        }
    }

    public void HideQuick()
    {
        if (!IsVisible) return;
        _focus.Hide(Hide);
        Topmost = _focus.EffectiveTopmost;
        HiddenByUser?.Invoke(this, EventArgs.Empty);
    }

    public async Task AttachRuntimeAsync(WindowsClientRuntime runtime)
    {
        if (_exiting) return;
        if (ReferenceEquals(_runtime, runtime)) return;
        _runtime = runtime;
        ViewModel.AttachRuntime(runtime);
        await RebindAsync();
    }

    public async Task RebindAsync(bool resetContent = false)
    {
        var runtime = _runtime;
        if (_exiting || runtime is null) return;
        _rebindCancellation?.Cancel();
        _rebindCancellation?.Dispose();
        _rebindCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _rebindCancellation.Token;
        var generation = ++_generation;
        ViewModel.SetViewerStatus(UiTextCatalog.Message("PreparingTheLocalWiki"));
        ViewModel.SetViewerReady(false);
        try
        {
            var active = await runtime.Application.ActiveWiki.GetActiveAsync(token);
            if (generation != _generation) return;
            var changedWiki = resetContent || (_activeWikiId != 0 && _activeWikiId != active.Id);
            _activeWikiId = active.Id;
            if (changedWiki)
            {
                _currentTitle = null;
                _wikiTitle = null;
                _sourceTitle = null;
                _pendingTitle = null;
            }
            ViewModel.SetHome(active.DefaultPageTitle, changedWiki);
            if (!active.IsPageDownloaded)
            {
                _webView.IsVisible = false;
                ViewModel.SetViewerStatus(UiTextCatalog.Message("ThisWikiHasNoDownloadedPagesDownloadOr"));
                return;
            }
            if (runtime.Viewer.Status.State != ViewerConsumerState.Ready)
                await runtime.Viewer.RetryAsync(token);
            if (generation != _generation) return;
            if (runtime.Viewer.Status is not { State: ViewerConsumerState.Ready, BaseUri: not null } ready)
                throw new InvalidOperationException("Viewer is unavailable.");
            _webView.IsVisible = true;
            var session = _session;
            if (session is null)
            {
                var bridge = new ViewerBridge(runtime.Application,
                    new WindowsExternalUriLauncher(), runtime.Preferences.IsSideButtonBackEnabled);
                _bridge = bridge;
                session = new ViewerWebViewSession(_webView, bridge);
                _session = session;
                session.PageCommitted += OnPageCommitted;
                session.StatusChanged += OnSessionStatusChanged;
                session.ProtocolError += OnProtocolError;
            }
            _bridge?.SetSideButtonBackEnabled(runtime.Preferences.IsSideButtonBackEnabled);
            session.Navigate(ready.BaseUri, active.Id, active.Title, forceReload: true);
            await session.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), token);
            if (!_firstDomReady)
            {
                _firstDomReady = true;
                _diagnostics.WriteQuickAccessStage(QuickAccessStage.DomReady,
                    _firstRequestClock.ElapsedMilliseconds);
            }
            if (generation != _generation || !ReferenceEquals(session, _session)) return;
            ViewModel.Reader.Attach(session.OpenPageAsync, session.BackAsync, session.HasHistory);
            ViewModel.SetViewerStatus(UiTextCatalog.Message("OpeningTheLocalWikiHomePage"));
            var pending = _pendingTitle;
            _pendingTitle = null;
            await session.OpenPageAsync(pending ?? _currentTitle ?? active.DefaultPageTitle, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (generation == _generation)
                ViewModel.SetViewerStatus(UiTextCatalog.Message("LocalPageContentIsTemporarilyUnavailableRetry"), failed: true);
        }
    }

    public async Task DetachRuntimeAsync()
    {
        ++_generation;
        _rebindCancellation?.Cancel();
        _rebindCancellation?.Dispose();
        _rebindCancellation = null;
        ViewModel.DetachRuntime();
        _runtime = null;
        var session = _session;
        _session = null;
        _bridge = null;
        if (session is null) return;
        session.PageCommitted -= OnPageCommitted;
        session.StatusChanged -= OnSessionStatusChanged;
        session.ProtocolError -= OnProtocolError;
        await session.DisposeAsync();
    }

    public async Task ApplyThemeAsync(ThemeVariant theme, bool isDark, bool contentChanged)
    {
        RequestedThemeVariant = theme;
        if (_session is { IsReady: true } session)
        {
            if (contentChanged) await RebindAsync();
            else await session.ChangeThemeAsync(isDark, _lifetime.Token);
        }
    }

    public void ApplySideButtonPreference(bool enabled) => _bridge?.SetSideButtonBackEnabled(enabled);

    public async Task DisposeAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _focus.Clear();
        _lifetime.Cancel();
        await DetachRuntimeAsync();
        ViewModel.Dispose();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _webView.EnvironmentRequested -= OnWebViewEnvironmentRequested;
        _webView.AdapterCreated -= OnAdapterCreated;
        _lifetime.Dispose();
        Close();
    }

    private async Task OpenArticleAsync(string title)
    {
        if (ViewModel.IsWiki) _wikiTitle = title;
        else _sourceTitle = title;
        _currentTitle = title;
        if (_session is not { IsReady: true } session)
        {
            _pendingTitle = title;
            ViewModel.SetViewerStatus(UiTextCatalog.Message("PreparingPageContentItWillOpenAutomaticallyWhen"));
            return;
        }
        try { await session.OpenPageAsync(title, _lifetime.Token); }
        catch { ViewModel.SetViewerStatus(UiTextCatalog.Message("CouldNotOpenPageContentRetry"), failed: true); }
    }

    private async Task RestoreSelectedPageAsync()
    {
        string? target = ViewModel.IsWiki
            ? ViewModel.IsReading ? _wikiTitle : ViewModel.IsHomeVisible ? ViewModel.HomeTitle : null
            : ViewModel.IsReading ? _sourceTitle : null;
        if (target is null) return;
        if (string.Equals(_currentTitle?.Split('#')[0], target.Split('#')[0], StringComparison.Ordinal))
            return;
        _currentTitle = target;
        if (_session is not { IsReady: true } session)
        {
            _pendingTitle = target;
            return;
        }
        try { await session.OpenPageAsync(target, _lifetime.Token); }
        catch { ViewModel.SetViewerStatus(UiTextCatalog.Message("CouldNotOpenPageContentRetry"), failed: true); }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(QuickAccessViewModel.IsReaderVisible)
            or nameof(QuickAccessViewModel.ViewerReady) or nameof(QuickAccessViewModel.ShowViewerOverlay))
            UpdateViewerSize();
        if (args.PropertyName == nameof(QuickAccessViewModel.IsChatVisible))
            FocusInput();
    }

    private void UpdateViewerSize()
    {
        var full = ViewModel.IsReaderVisible && !ViewModel.ShowViewerOverlay;
        _webView.Width = full ? double.NaN : 1;
        _webView.Height = full ? double.NaN : 1;
        _webView.HorizontalAlignment = full ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Right;
        _webView.VerticalAlignment = full ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Bottom;
    }

    private void OnPageCommitted(object? sender, ViewerPageIdentity identity)
    {
        var session = _session;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(sender, _session) || session?.CurrentPage != identity) return;
            _currentTitle = identity.CanonicalTitle;
            if (!_firstPageCommitted)
            {
                _firstPageCommitted = true;
                _diagnostics.WriteQuickAccessStage(QuickAccessStage.FirstPageCommitted,
                    _firstRequestClock.ElapsedMilliseconds);
            }
            ViewModel.SetViewerReady(true);
            ViewModel.SetViewerStatus(UiTextCatalog.Message("Opened3", identity.CanonicalTitle));
            _ = ViewModel.Reader.ApplyPageIdentityAsync(identity);
        });
    }

    private void OnAdapterCreated(object? sender, EventArgs args)
    {
        if (_firstAdapterCreated) return;
        _firstAdapterCreated = true;
        _diagnostics.WriteQuickAccessStage(QuickAccessStage.AdapterCreated,
            _firstRequestClock.ElapsedMilliseconds);
    }

    private void OnSessionStatusChanged(object? sender, string message) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, _session)) ViewModel.SetViewerStatus(message);
        });

    private void OnProtocolError(object? sender, ViewerProtocolError error) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, _session))
                ViewModel.HandleViewerProtocolError(error.Code);
        });

    private static void OnWebViewEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs args)
    {
        if (args is WindowsWebView2EnvironmentRequestedEventArgs webView2)
            webView2.UserDataFolder = ReaderView.GetDefaultUserDataFolder();
    }

    private void OnQuestionKeyDown(object? sender, KeyEventArgs args)
    {
        var composing = _questionBox.GetVisualDescendants().OfType<TextPresenter>()
            .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
        if (!ChatInputBehavior.ShouldSend(args.Key, args.KeyModifiers, composing)) return;
        if (!ViewModel.Chat.SendCommand.CanExecute(null)) return;
        ViewModel.Chat.SendCommand.Execute(null);
        args.Handled = true;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        if (ViewModel.Wiki.SearchCommand.CanExecute(null))
            ViewModel.Wiki.SearchCommand.Execute(null);
        args.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        HideQuick();
        args.Handled = true;
    }

    private void OnHideClick(object? sender, RoutedEventArgs args) => HideQuick();

    private void OnNewConversationClick(object? sender, RoutedEventArgs args) => FocusInput();

    private void FocusInput()
    {
        if (_exiting || !IsVisible || !IsActive) return;
        _focus.InvalidateFocus();
        var version = _focus.FocusVersion;
        var wiki = ViewModel.IsWiki;
        var chat = ViewModel.IsChatVisible;
        if (!_focus.CanFocus(version, IsActive)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_exiting || !IsVisible || !_focus.CanFocus(version, IsActive)
                || wiki != ViewModel.IsWiki || chat != ViewModel.IsChatVisible) return;
            if (wiki) _searchBox.Focus();
            else if (chat) _questionBox.Focus();
        });
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Only the empty header is a drag handle; inputs and buttons keep normal interaction.
        if (args.Source is Visual source && source.GetVisualAncestors().Prepend(source)
                .Any(control => control is Button or TextBox)) return;
        BeginMoveDrag(args);
        args.Handled = true;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true;
        HideQuick();
    }
}
