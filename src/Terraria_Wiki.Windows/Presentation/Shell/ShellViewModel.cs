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

internal enum StartupRecoveryKind
{
    None,
    Retry,
    SelectDataRoot,
    InstallWebView2,
}

internal sealed class ShellViewModel : ObservableObject, IDisposable
{
    public const double WideReaderThreshold = 1100;

    private ShellSection _currentSection = ShellSection.Chat;
    private ReaderPaneState _readerState = ReaderPaneState.Home;
    private bool _isWideLayout = true;
    private string _startupStatus = "正在启动…";
    private bool _hasStartupError;
    private StartupRecoveryKind _startupRecovery;
    private bool _disposed;

    public ShellViewModel(
        IChatConversationStore? chatConversationStore = null,
        Func<CancellationToken, Task<string?>>? pickImportPackage = null,
        Func<WikiBookSnapshot, CancellationToken, Task<WikiPackageExportResponse?>>? exportPackage = null,
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
            pickImportPackage,
            exportPackage,
            () => SelectDataRootRequested?.Invoke(this, EventArgs.Empty));
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
        NavigateChatCommand = new RelayCommand(_ => NavigateChat());
        NavigateFavoritesCommand = new RelayCommand(_ => NavigateFavorites());
        NavigateLibraryCommand = new RelayCommand(_ => NavigateLibrary());
        NavigateManagementCommand = new RelayCommand(_ => NavigateManagement());
        NavigateSettingsCommand = new RelayCommand(_ => NavigateSettings());
        OpenWikiReaderCommand = new RelayCommand(_ => OpenWikiReader());
        CloseReaderCommand = new RelayCommand(_ => CloseReader(), _ => IsReaderVisible);
        RetryStartupCommand = new RelayCommand(
            _ => RetryStartupRequested?.Invoke(this, EventArgs.Empty),
            _ => CanRetryStartup);
        GetWebView2Command = new RelayCommand(
            _ => GetWebView2Requested?.Invoke(this, EventArgs.Empty),
            _ => CanGetWebView2);
    }

    public event EventHandler? RetryStartupRequested;
    public event EventHandler? SelectDataRootRequested;
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
            if (!SetProperty(ref _currentSection, value))
                return;
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

    public bool CanRetryStartup => HasStartupError && StartupRecovery != StartupRecoveryKind.SelectDataRoot;
    public bool CanGetWebView2 => HasStartupError && StartupRecovery == StartupRecoveryKind.InstallWebView2;

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
    public bool IsReaderVisible => !IsSettingsSelected
                                   && (ReaderState switch
                                   {
                                       ReaderPaneState.Home => IsWideLayout,
                                       ReaderPaneState.ChatSource or ReaderPaneState.Wiki => true,
                                       _ => false,
                                   });
    public bool IsReaderOverlayVisible => IsReaderVisible && !IsWideLayout;
    public bool IsReaderInlineVisible => IsReaderVisible && IsWideLayout;
    public bool CanCloseReader => IsReaderVisible;

    public RelayCommand NavigateChatCommand { get; }
    public RelayCommand NavigateFavoritesCommand { get; }
    public RelayCommand NavigateLibraryCommand { get; }
    public RelayCommand NavigateManagementCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public RelayCommand OpenWikiReaderCommand { get; }
    public RelayCommand CloseReaderCommand { get; }
    public RelayCommand RetryStartupCommand { get; }
    public RelayCommand GetWebView2Command { get; }

    public void SetViewportWidth(double width) => IsWideLayout = width >= WideReaderThreshold;

    public void SetWebView2Status(WebView2RuntimeStatus status) => Settings.Diagnostics.SetWebView2Status(status);

    public void AttachRuntime(WindowsClientRuntime runtime)
    {
        var gateway = new WindowsChatRuntimeGateway(runtime);
        Chat.AttachRuntime(gateway, gateway);
        Wiki.Attach((query, token) => runtime.Application.Query.SearchAsync(query, cancellationToken: token));
        Favorites.AttachWikiLibrary(runtime.Application.Library);
        Library.Attach(runtime.Application.Library);
        Management.SetDataRoot(runtime.DataRoot);
        Management.Attach(runtime.Application);
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
        Func<CancellationToken, Task> goBack) => Reader.Attach(openPage, goBack);

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

    public void ShowDataRootRecovery(string message)
    {
        Management.ReportDataRootSelection(message);
        SetStartupStatus(
            message,
            isError: true,
            recovery: StartupRecoveryKind.SelectDataRoot);
        NavigateManagement();
    }

    public void ShowEmptyWikiOnboarding()
    {
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
        CurrentSection = ShellSection.Chat;
        if (!isLeavingSettings && ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
    }

    public void NavigateFavorites()
    {
        var isLeavingSettings = IsSettingsSelected;
        CurrentSection = ShellSection.Favorites;
        if (!isLeavingSettings && ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
        _ = Favorites.RefreshAsync();
    }

    public void NavigateLibrary()
    {
        var isLeavingSettings = IsSettingsSelected;
        CurrentSection = ShellSection.Library;
        if (!isLeavingSettings && ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
        _ = Library.RefreshAsync();
    }

    public void NavigateManagement()
    {
        var isLeavingSettings = IsSettingsSelected;
        CurrentSection = ShellSection.Management;
        if (!isLeavingSettings && ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
        _ = Management.RefreshAsync();
    }

    public void NavigateSettings()
    {
        CurrentSection = ShellSection.Settings;
    }

    public void RefreshAfterWikiDataChange()
    {
        Wiki.ResetForDataSourceChange();
        _ = Favorites.RefreshAsync();
        if (IsLibrarySelected)
            _ = Library.RefreshAsync();
    }

    public void OpenWikiReader() => ReaderState = ReaderPaneState.Wiki;

    public void CloseReader() => ReaderState = ReaderPaneState.Closed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Chat.ConversationChanged -= OnConversationChanged;
        Favorites.ConversationFavoriteChanged -= OnConversationFavoriteChanged;
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
        await Reader.OpenAsync(new ReaderPageRequest(result.Title));
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

    private async Task OpenFavoriteWikiAsync(string title)
    {
        ReaderState = ReaderPaneState.Wiki;
        await Reader.OpenAsync(new ReaderPageRequest(title));
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
}
