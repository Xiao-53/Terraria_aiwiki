using System.ComponentModel;
using Terraria_Wiki.Application;
using Terraria_Wiki.Models;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Chat;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Favorites;
using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Terraria_Wiki.Windows.Presentation.Library;
using Terraria_Wiki.Windows.Presentation.Management;
using Terraria_Wiki.Windows.Presentation.Markdown;
using Terraria_Wiki.Windows.Presentation.Reader;
using Terraria_Wiki.Windows.Presentation.Settings;
using Terraria_Wiki.Windows.Presentation.Wiki;
using Terraria_Wiki.Windows.Updates;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows.Presentation.Shell;

internal enum ShellSection
{
    Chat,
    Favorites,
    Library,
    Management,
    Settings,
    Wiki,
}

internal enum WikiDisplayMode
{
    Home,
    Search,
    Reading,
}

internal enum StartupRecoveryKind
{
    None,
    Retry,
    InstallWebView2,
}

internal sealed class ShellViewModel : ObservableObject, IDisposable
{
    public const double WideReaderThreshold = 1100;

    private ShellSection _currentSection = ShellSection.Chat;
    private ReaderPaneState _readerState = ReaderPaneState.Home;
    private ReaderPaneState _readerStateBeforeWiki = ReaderPaneState.Home;
    private WikiDisplayMode _wikiMode = WikiDisplayMode.Home;
    private string? _wikiHomeTitle;
    private bool _isWideLayout = true;
    private string _startupStatus = "正在启动…";
    private bool _hasStartupError;
    private StartupRecoveryKind _startupRecovery;
    private bool _disposed;
    private Func<int, CancellationToken, Task<WikiBookSnapshot>>? _switchWiki;

    public ShellViewModel(
        IChatConversationStore? chatConversationStore = null,
        Func<CancellationToken, Task<string?>>? pickImportDatabase = null,
        Func<WikiBookSnapshot, CancellationToken, Task<WikiDatabaseExportResponse?>>? exportDatabase = null,
        Func<ApplicationPreferences, Task>? applyPreferences = null,
        Func<CancellationToken, Task<AiRuntimeConfigurationDraft>>? loadAiConfiguration = null,
        Func<AiRuntimeConfigurationDraft, CancellationToken, Task<AiRuntimeConfiguration>>? saveAiConfiguration = null,
        Func<CancellationToken, Task<WindowsAiRuntimeStatus>>? restartAi = null,
        Func<CancellationToken, Task<WindowsAiRuntimeStatus>>? clearAiCredential = null,
        IProductUpdateService? productUpdateService = null,
        Func<Task>? applyUpdateAndRestart = null,
        Func<Uri, Task>? openExternalUri = null,
        ProductDiagnostics? productDiagnostics = null)
    {
        Reader = new ReaderPaneViewModel();
        var conversationStore = chatConversationStore ?? new MemoryChatConversationStore();
        Chat = new ChatViewModel(
            new SafeMarkdownParser(),
            OpenSourceAsync,
            OpenWikiReader,
            conversationStore);
        Wiki = new WikiViewModel(OpenSearchResultAsync);
        Favorites = new FavoritesViewModel(
            conversationStore,
            OpenChatConversationAsync,
            OpenFavoriteWikiAsync);
        Library = new WikiLibraryViewModel(
            conversationStore,
            OpenLibraryWikiAsync,
            OpenChatConversationAsync,
            Chat.MarkConversationDeleted);
        Management = new WikiManagementViewModel(
            pickImportDatabase,
            exportDatabase);
        Settings = new SettingsViewModel(
            applyPreferences,
            loadAiConfiguration,
            saveAiConfiguration,
            restartAi,
            clearAiCredential,
            productUpdateService,
            applyUpdateAndRestart,
            openExternalUri,
            productDiagnostics);
        Chat.ConversationChanged += OnConversationChanged;
        Favorites.ConversationFavoriteChanged += OnConversationFavoriteChanged;
        Wiki.PropertyChanged += OnWikiPropertyChanged;
        NavigateChatCommand = new RelayCommand(_ => NavigateChat());
        NavigateFavoritesCommand = new RelayCommand(_ => NavigateFavorites());
        NavigateLibraryCommand = new RelayCommand(_ => NavigateLibrary());
        NavigateManagementCommand = new RelayCommand(_ => NavigateManagement());
        NavigateSettingsCommand = new RelayCommand(_ => NavigateSettings());
        NavigateWikiCommand = new RelayCommand(_ => NavigateWiki());
        GoToWikiHomeCommand = new RelayCommand(_ => GoToWikiHome());
        ReturnToWikiSearchCommand = new RelayCommand(_ => ReturnToWikiSearch());
        OpenWikiReaderCommand = new RelayCommand(_ => OpenWikiReader(), _ => CanOpenWikiReader);
        CloseReaderCommand = new RelayCommand(_ => CloseReader(), _ => CanCloseReader);
        RetryStartupCommand = new RelayCommand(
            _ => RetryStartupRequested?.Invoke(this, EventArgs.Empty),
            _ => CanRetryStartup);
        GetWebView2Command = new RelayCommand(
            _ => GetWebView2Requested?.Invoke(this, EventArgs.Empty),
            _ => CanGetWebView2);
        Reader.PropertyChanged += OnReaderPropertyChanged;
    }

    public event EventHandler? RetryStartupRequested;
    public event EventHandler? GetWebView2Requested;

    public ChatViewModel Chat { get; }
    public WikiViewModel Wiki { get; }
    public FavoritesViewModel Favorites { get; }
    public WikiLibraryViewModel Library { get; }
    public WikiManagementViewModel Management { get; }
    public SettingsViewModel Settings { get; }
    public ReaderPaneViewModel Reader { get; }

    public ShellSection CurrentSection
    {
        get => _currentSection;
        private set
        {
            var previous = _currentSection;
            if (!SetProperty(ref _currentSection, value))
                return;
            if (previous == ShellSection.Wiki && value != ShellSection.Wiki)
                ReaderState = _readerStateBeforeWiki;
            OnPropertyChanged(nameof(IsChatSelected));
            OnPropertyChanged(nameof(IsFavoritesSelected));
            OnPropertyChanged(nameof(IsLibrarySelected));
            OnPropertyChanged(nameof(IsManagementSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsWikiSelected));
            OnPropertyChanged(nameof(IsChatVisible));
            OnPropertyChanged(nameof(IsFavoritesVisible));
            OnPropertyChanged(nameof(IsLibraryVisible));
            OnPropertyChanged(nameof(IsManagementVisible));
            OnPropertyChanged(nameof(IsSettingsVisible));
            OnPropertyChanged(nameof(IsWikiVisible));
            OnPropertyChanged(nameof(IsWikiHomeActive));
            OnPropertyChanged(nameof(IsWikiSearchActive));
            OnPropertyChanged(nameof(IsWikiReadingActive));
            OnPropertyChanged(nameof(IsWikiSurfaceVisible));
            OnPropertyChanged(nameof(IsWikiPreviewVisible));
            RefreshReaderProperties();
        }
    }

    public ReaderPaneState ReaderState
    {
        get => _readerState;
        private set
        {
            if (SetProperty(ref _readerState, value))
                RefreshReaderProperties();
        }
    }

    public bool IsWideLayout
    {
        get => _isWideLayout;
        private set
        {
            if (SetProperty(ref _isWideLayout, value))
                RefreshReaderProperties();
        }
    }

    public string StartupStatus
    {
        get => _startupStatus;
        private set => SetProperty(ref _startupStatus, value);
    }

    public bool HasStartupError
    {
        get => _hasStartupError;
        private set
        {
            if (SetProperty(ref _hasStartupError, value))
            {
                RetryStartupCommand.NotifyCanExecuteChanged();
                GetWebView2Command.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanRetryStartup));
                OnPropertyChanged(nameof(CanGetWebView2));
            }
        }
    }

    public StartupRecoveryKind StartupRecovery
    {
        get => _startupRecovery;
        private set
        {
            if (!SetProperty(ref _startupRecovery, value))
                return;
            OnPropertyChanged(nameof(CanRetryStartup));
            OnPropertyChanged(nameof(CanGetWebView2));
            RetryStartupCommand.NotifyCanExecuteChanged();
            GetWebView2Command.NotifyCanExecuteChanged();
        }
    }

    public bool CanRetryStartup => HasStartupError;
    public bool CanGetWebView2 => HasStartupError && StartupRecovery == StartupRecoveryKind.InstallWebView2;
    public bool CanOpenWikiReader => Reader.IsReady;

    public bool IsChatSelected => CurrentSection == ShellSection.Chat;
    public bool IsFavoritesSelected => CurrentSection == ShellSection.Favorites;
    public bool IsLibrarySelected => CurrentSection == ShellSection.Library;
    public bool IsManagementSelected => CurrentSection == ShellSection.Management;
    public bool IsSettingsSelected => CurrentSection == ShellSection.Settings;
    public bool IsWikiSelected => CurrentSection == ShellSection.Wiki;
    public bool IsChatVisible => IsChatSelected;
    public bool IsFavoritesVisible => IsFavoritesSelected;
    public bool IsLibraryVisible => IsLibrarySelected;
    public bool IsManagementVisible => IsManagementSelected;
    public bool IsSettingsVisible => IsSettingsSelected;
    public bool IsWikiVisible => IsWikiSelected;
    public WikiDisplayMode WikiMode
    {
        get => _wikiMode;
        private set
        {
            if (!SetProperty(ref _wikiMode, value))
                return;
            OnPropertyChanged(nameof(IsWikiHomeActive));
            OnPropertyChanged(nameof(IsWikiSearchActive));
            OnPropertyChanged(nameof(IsWikiReadingActive));
            OnPropertyChanged(nameof(IsWikiSurfaceVisible));
            RefreshReaderProperties();
        }
    }
    public bool IsWikiHomeActive => IsWikiSelected && WikiMode == WikiDisplayMode.Home;
    public bool IsWikiSearchActive => IsWikiSelected && WikiMode == WikiDisplayMode.Search;
    public bool IsWikiReadingActive => IsWikiSelected && WikiMode == WikiDisplayMode.Reading;
    public bool IsWikiSurfaceVisible => IsWikiSelected && WikiMode != WikiDisplayMode.Search;
    public bool IsWikiPreviewVisible => !IsSettingsSelected && !IsWikiSelected;
    public bool IsReaderVisible => IsWikiSelected
        ? IsWikiSurfaceVisible
        : !IsSettingsSelected
          && (ReaderState switch
          {
              ReaderPaneState.Home => IsWideLayout,
              ReaderPaneState.ChatSource or ReaderPaneState.Wiki => true,
              _ => false,
          });
    public bool IsReaderOverlayVisible => !IsWikiSelected && IsReaderVisible && !IsWideLayout;
    public bool IsReaderInlineVisible => !IsWikiSelected && IsReaderVisible && IsWideLayout;
    public bool CanCloseReader => !IsWikiSelected && IsReaderVisible;

    public RelayCommand NavigateChatCommand { get; }
    public RelayCommand NavigateFavoritesCommand { get; }
    public RelayCommand NavigateLibraryCommand { get; }
    public RelayCommand NavigateManagementCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public RelayCommand NavigateWikiCommand { get; }
    public RelayCommand GoToWikiHomeCommand { get; }
    public RelayCommand ReturnToWikiSearchCommand { get; }
    public RelayCommand OpenWikiReaderCommand { get; }
    public RelayCommand CloseReaderCommand { get; }
    public RelayCommand RetryStartupCommand { get; }
    public RelayCommand GetWebView2Command { get; }

    public void SetViewportWidth(double width) => IsWideLayout = width >= WideReaderThreshold;

    public void SetWebView2Status(WebView2RuntimeStatus status) => Settings.Diagnostics.SetWebView2Status(status);

    public void AttachRuntime(
        WindowsClientRuntime runtime,
        Func<int, CancellationToken, Task<WikiBookSnapshot>>? switchWiki = null)
    {
        _switchWiki = switchWiki;
        var gateway = new WindowsChatRuntimeGateway(runtime);
        Chat.AttachRuntime(gateway, gateway);
        Wiki.Attach((query, token) => runtime.Application.Query.SearchAsync(query, cancellationToken: token));
        Favorites.AttachWikiLibrary(runtime.Application.Library);
        Library.Attach(runtime.Application.Library);
        Management.Attach(runtime.Application, switchWiki);
        Settings.Attach(runtime.Application.Settings);
        Reader.AttachFavorites(Favorites.IsWikiFavoriteAsync, Favorites.SetWikiFavoriteAsync);
        _ = Favorites.RefreshAsync();
        _ = Settings.RefreshAsync();
        if (IsLibrarySelected)
            _ = Library.RefreshAsync();
        if (IsManagementSelected)
            _ = Management.RefreshAsync();
    }

    public void AttachReader(
        Func<string, CancellationToken, Task> openPage,
        Func<CancellationToken, Task> goBack,
        bool canGoBack = false) => Reader.Attach(openPage, goBack, canGoBack);

    public void DetachRuntime()
    {
        Chat.DetachRuntime();
        Wiki.Detach();
        Favorites.DetachWikiLibrary();
        Library.Detach();
        Management.Detach();
        Settings.Detach();
        Reader.DetachFavorites();
        Reader.Detach();
        _switchWiki = null;
    }

    public void SetStartupStatus(
        string message,
        bool isError = false,
        StartupRecoveryKind recovery = StartupRecoveryKind.Retry)
    {
        StartupStatus = message;
        StartupRecovery = isError ? recovery : StartupRecoveryKind.None;
        HasStartupError = isError;
    }

    public void ShowEmptyWikiOnboarding()
    {
        _wikiHomeTitle = null;
        Reader.Detach();
        Reader.SetStatus("当前 Wiki 尚未下载页面；请先在数据管理中下载或导入。");
        CloseReader();
        Management.ShowEmptyWikiOnboarding();
        SetStartupStatus("当前 Wiki 尚无页面内容 · 请在数据管理中下载或导入");
        NavigateManagement();
    }

    public bool TryCloseOverlay()
    {
        if (!IsReaderOverlayVisible)
            return false;
        CloseReader();
        return true;
    }

    public void NavigateChat()
    {
        var isLeavingSettings = IsSettingsSelected;
        var isLeavingWiki = IsWikiSelected;
        CurrentSection = ShellSection.Chat;
        if (!isLeavingSettings && !isLeavingWiki && ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
    }

    public void NavigateFavorites()
    {
        var isLeavingSettings = IsSettingsSelected;
        var isLeavingWiki = IsWikiSelected;
        CurrentSection = ShellSection.Favorites;
        if (!isLeavingSettings && !isLeavingWiki && ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
        _ = Favorites.RefreshAsync();
    }

    public void NavigateLibrary()
    {
        var isLeavingSettings = IsSettingsSelected;
        var isLeavingWiki = IsWikiSelected;
        CurrentSection = ShellSection.Library;
        if (!isLeavingSettings && !isLeavingWiki && ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
        _ = Library.RefreshAsync();
    }

    public void NavigateManagement()
    {
        var isLeavingSettings = IsSettingsSelected;
        var isLeavingWiki = IsWikiSelected;
        CurrentSection = ShellSection.Management;
        if (!isLeavingSettings && !isLeavingWiki && ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
        _ = Management.RefreshAsync();
    }

    public void NavigateSettings()
    {
        CurrentSection = ShellSection.Settings;
    }

    public void NavigateWiki()
    {
        if (IsWikiSelected)
            return;
        _readerStateBeforeWiki = ReaderState;
        CurrentSection = ShellSection.Wiki;
        if (string.IsNullOrWhiteSpace(Wiki.Query))
        {
            WikiMode = WikiDisplayMode.Home;
            _ = OpenWikiHomeAsync();
        }
        else
        {
            WikiMode = WikiDisplayMode.Search;
        }
    }

    public void RefreshAfterWikiDataChange(bool clearHomePage)
    {
        if (clearHomePage)
            _wikiHomeTitle = null;
        Wiki.ResetForDataSourceChange();
        _ = Favorites.RefreshAsync();
        if (IsLibrarySelected)
            _ = Library.RefreshAsync();
    }

    public void OpenWikiReader()
    {
        if (CanOpenWikiReader)
            ReaderState = ReaderPaneState.Wiki;
    }

    public void CloseReader() => ReaderState = ReaderPaneState.Closed;

    public void SetWikiHomePage(string title) =>
        _wikiHomeTitle = string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    public void OnViewerPageCommitted(ViewerPageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!IsWikiSelected || WikiMode == WikiDisplayMode.Search)
            return;
        WikiMode = string.Equals(identity.CanonicalTitle, _wikiHomeTitle, StringComparison.Ordinal)
            ? WikiDisplayMode.Home
            : WikiDisplayMode.Reading;
    }

    public void GoToWikiHome()
    {
        if (!IsWikiSelected)
            return;
        if (Wiki.Query.Length > 0)
        {
            Wiki.Query = string.Empty;
            return;
        }
        WikiMode = WikiDisplayMode.Home;
        _ = OpenWikiHomeAsync();
    }

    public void ReturnToWikiSearch()
    {
        if (IsWikiSelected && WikiMode == WikiDisplayMode.Reading)
            WikiMode = WikiDisplayMode.Search;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Reader.PropertyChanged -= OnReaderPropertyChanged;
        Chat.ConversationChanged -= OnConversationChanged;
        Favorites.ConversationFavoriteChanged -= OnConversationFavoriteChanged;
        Wiki.PropertyChanged -= OnWikiPropertyChanged;
        Chat.Dispose();
        Wiki.Dispose();
        Library.Dispose();
        Management.Dispose();
        Settings.Dispose();
        Reader.Detach();
    }

    private async Task OpenSourceAsync(AiAgentSource source)
    {
        ReaderState = ReaderPaneState.ChatSource;
        await Reader.OpenAsync(new ReaderPageRequest(source.ResolvedTitle, source.Anchor));
    }

    private async Task OpenSearchResultAsync(SearchResultItem result)
    {
        ReaderState = ReaderPaneState.Wiki;
        if (IsWikiSelected)
            WikiMode = WikiDisplayMode.Reading;
        var title = string.IsNullOrWhiteSpace(result.CanonicalTitle)
            ? string.IsNullOrWhiteSpace(result.RedirectTo) ? result.Title : result.RedirectTo
            : result.CanonicalTitle;
        await Reader.OpenAsync(new ReaderPageRequest(
            title,
            string.IsNullOrWhiteSpace(result.Anchor) ? null : result.Anchor));
    }

    private async Task OpenWikiHomeAsync()
    {
        var homeTitle = _wikiHomeTitle;
        if (string.IsNullOrWhiteSpace(homeTitle) || !Reader.IsReady)
            return;
        await Reader.OpenAsync(new ReaderPageRequest(homeTitle));
    }

    private void OnWikiPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(WikiViewModel.Query) || !IsWikiSelected)
            return;
        if (string.IsNullOrWhiteSpace(Wiki.Query))
        {
            if (WikiMode != WikiDisplayMode.Home)
            {
                WikiMode = WikiDisplayMode.Home;
                _ = OpenWikiHomeAsync();
            }
        }
        else if (WikiMode != WikiDisplayMode.Search)
        {
            WikiMode = WikiDisplayMode.Search;
        }
    }

    private Task OpenChatConversationAsync(ChatConversation conversation)
    {
        if (Chat.LoadConversation(conversation))
            NavigateChat();
        return Task.CompletedTask;
    }

    private void OnConversationChanged(Guid? id)
    {
        _ = Favorites.RefreshAsync();
        if (IsLibrarySelected)
            _ = Library.RefreshAsync();
    }

    private void OnConversationFavoriteChanged(Guid id, bool isFavorite)
    {
        Chat.SetFavoriteState(id, isFavorite);
        if (IsLibrarySelected)
            _ = Library.RefreshAsync();
    }

    private async Task OpenFavoriteWikiAsync(GlobalWikiFavoriteItem item)
    {
        ReaderState = ReaderPaneState.Wiki;
        if (_switchWiki is not null)
            await _switchWiki(item.WikiId, CancellationToken.None);
        await Reader.OpenAsync(new ReaderPageRequest(item.PageTitle));
    }

    private async Task OpenLibraryWikiAsync(string title)
    {
        ReaderState = ReaderPaneState.Wiki;
        await Reader.OpenAsync(new ReaderPageRequest(title));
    }

    private void RefreshReaderProperties()
    {
        OnPropertyChanged(nameof(IsReaderVisible));
        OnPropertyChanged(nameof(IsReaderOverlayVisible));
        OnPropertyChanged(nameof(IsReaderInlineVisible));
        OnPropertyChanged(nameof(CanCloseReader));
        CloseReaderCommand.NotifyCanExecuteChanged();
    }

    private void OnReaderPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (!string.Equals(args.PropertyName, nameof(ReaderPaneViewModel.IsReady), StringComparison.Ordinal))
            return;
        OnPropertyChanged(nameof(CanOpenWikiReader));
        OpenWikiReaderCommand.NotifyCanExecuteChanged();
        if (Reader.IsReady && IsWikiHomeActive)
            _ = OpenWikiHomeAsync();
    }
}
