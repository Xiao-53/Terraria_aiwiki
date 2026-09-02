using Terraria_Wiki.Models;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Chat;
using Terraria_Wiki.Windows.Presentation.Favorites;
using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Terraria_Wiki.Windows.Presentation.Library;
using Terraria_Wiki.Windows.Presentation.Markdown;
using Terraria_Wiki.Windows.Presentation.Reader;
using Terraria_Wiki.Windows.Presentation.Wiki;

namespace Terraria_Wiki.Windows.Presentation.Shell;

internal enum ShellSection
{
    Chat,
    Favorites,
    Library,
    Wiki,
}

internal sealed class ShellViewModel : ObservableObject, IDisposable
{
    public const double WideReaderThreshold = 1100;

    private ShellSection _currentSection = ShellSection.Chat;
    private ReaderPaneState _readerState = ReaderPaneState.Home;
    private bool _isWideLayout = true;
    private string _startupStatus = "正在启动…";
    private bool _hasStartupError;
    private bool _disposed;

    public ShellViewModel(IChatFavoriteStore? chatFavoriteStore = null)
    {
        Reader = new ReaderPaneViewModel();
        Chat = new ChatViewModel(
            new SafeMarkdownParser(),
            OpenSourceAsync,
            OpenWikiReader,
            ToggleChatFavoriteAsync);
        Wiki = new WikiViewModel(OpenSearchResultAsync);
        Favorites = new FavoritesViewModel(
            chatFavoriteStore ?? new MemoryChatFavoriteStore(),
            OpenChatFavoriteAsync,
            OpenFavoriteWikiAsync);
        Library = new WikiLibraryViewModel(OpenLibraryWikiAsync);
        Favorites.ChatFavoriteRemoved += Chat.MarkFavoriteRemoved;
        NavigateChatCommand = new RelayCommand(_ => NavigateChat());
        NavigateFavoritesCommand = new RelayCommand(_ => NavigateFavorites());
        NavigateLibraryCommand = new RelayCommand(_ => NavigateLibrary());
        OpenWikiReaderCommand = new RelayCommand(_ => OpenWikiReader());
        CloseReaderCommand = new RelayCommand(_ => CloseReader(), _ => IsReaderVisible);
        RetryStartupCommand = new RelayCommand(_ => RetryStartupRequested?.Invoke(this, EventArgs.Empty), _ => HasStartupError);
        SelectDataRootCommand = new RelayCommand(
            _ => SelectDataRootRequested?.Invoke(this, EventArgs.Empty),
            _ => HasStartupError);
    }

    public event EventHandler? RetryStartupRequested;
    public event EventHandler? SelectDataRootRequested;

    public ChatViewModel Chat { get; }
    public WikiViewModel Wiki { get; }
    public FavoritesViewModel Favorites { get; }
    public WikiLibraryViewModel Library { get; }
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
            OnPropertyChanged(nameof(IsWikiSelected));
            OnPropertyChanged(nameof(IsChatVisible));
            OnPropertyChanged(nameof(IsFavoritesVisible));
            OnPropertyChanged(nameof(IsLibraryVisible));
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
                SelectDataRootCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsChatSelected => CurrentSection == ShellSection.Chat;
    public bool IsFavoritesSelected => CurrentSection == ShellSection.Favorites;
    public bool IsLibrarySelected => CurrentSection == ShellSection.Library;
    public bool IsWikiSelected => CurrentSection == ShellSection.Wiki;
    public bool IsChatVisible => IsChatSelected;
    public bool IsFavoritesVisible => IsFavoritesSelected;
    public bool IsLibraryVisible => IsLibrarySelected;
    public bool IsWikiVisible => IsWikiSelected;
    public bool IsReaderVisible => ReaderState switch
    {
        ReaderPaneState.Home => IsWideLayout,
        ReaderPaneState.ChatSource or ReaderPaneState.Wiki => true,
        _ => false,
    };
    public bool IsReaderOverlayVisible => IsReaderVisible && !IsWideLayout;
    public bool IsReaderInlineVisible => IsReaderVisible && IsWideLayout;
    public bool CanCloseReader => IsReaderVisible;

    public RelayCommand NavigateChatCommand { get; }
    public RelayCommand NavigateFavoritesCommand { get; }
    public RelayCommand NavigateLibraryCommand { get; }
    public RelayCommand OpenWikiReaderCommand { get; }
    public RelayCommand CloseReaderCommand { get; }
    public RelayCommand RetryStartupCommand { get; }
    public RelayCommand SelectDataRootCommand { get; }

    public void SetViewportWidth(double width) => IsWideLayout = width >= WideReaderThreshold;

    public void AttachRuntime(WindowsClientRuntime runtime)
    {
        var gateway = new WindowsChatRuntimeGateway(runtime);
        Chat.AttachRuntime(gateway, gateway);
        Wiki.Attach((query, token) => runtime.Application.Query.SearchAsync(query, cancellationToken: token));
        Favorites.AttachWikiLibrary(runtime.Application.Library);
        Library.Attach(runtime.Application.Library);
        Reader.AttachFavorites(Favorites.IsWikiFavoriteAsync, Favorites.SetWikiFavoriteAsync);
        _ = Favorites.RefreshAsync();
        if (IsLibrarySelected)
            _ = Library.RefreshAsync();
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
        Reader.DetachFavorites();
        Reader.Detach();
    }

    public void SetStartupStatus(string message, bool isError = false)
    {
        StartupStatus = message;
        HasStartupError = isError;
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
        CurrentSection = ShellSection.Chat;
        if (ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
    }

    public void NavigateFavorites()
    {
        CurrentSection = ShellSection.Favorites;
        if (ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
        _ = Favorites.RefreshAsync();
    }

    public void NavigateLibrary()
    {
        CurrentSection = ShellSection.Library;
        if (ReaderState == ReaderPaneState.Wiki)
            ReaderState = ReaderPaneState.Home;
        _ = Library.RefreshAsync();
    }

    public void OpenWikiReader() => ReaderState = ReaderPaneState.Wiki;

    public void CloseReader() => ReaderState = ReaderPaneState.Closed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Favorites.ChatFavoriteRemoved -= Chat.MarkFavoriteRemoved;
        Chat.Dispose();
        Wiki.Dispose();
        Library.Dispose();
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

    private async Task<bool> ToggleChatFavoriteAsync(ChatFavoriteRecord record, bool isFavorite) =>
        await Favorites.ToggleChatAsync(record, isFavorite);

    private Task OpenChatFavoriteAsync(ChatFavoriteRecord record)
    {
        if (Chat.LoadFavorite(record))
            NavigateChat();
        return Task.CompletedTask;
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
