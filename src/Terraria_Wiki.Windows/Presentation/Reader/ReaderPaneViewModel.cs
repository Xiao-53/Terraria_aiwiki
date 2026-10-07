using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows.Presentation.Reader;

internal enum ReaderPaneState
{
    Closed,
    Home,
    ChatSource,
    Wiki,
}

internal sealed record ReaderPageRequest(string Title, string? Anchor = null)
{
    public string Target => string.IsNullOrWhiteSpace(Anchor)
        ? Title
        : $"{Title}#{Anchor.TrimStart('#')}";
}

internal sealed record ReaderFavoriteUpdate(bool Succeeded, bool IsFavorite);

internal sealed class ReaderPaneViewModel : ObservableObject
{
    private Func<string, CancellationToken, Task>? _openPage;
    private Func<CancellationToken, Task>? _goBack;
    private Func<ViewerPageIdentity, Task<bool>>? _isFavorite;
    private Func<ViewerPageIdentity, bool, Task<ReaderFavoriteUpdate>>? _setFavorite;
    private string _title = "Wiki";
    private string _location = UiTextCatalog.Message("WaitingForTheLocalViewer");
    private string _statusMessage = UiTextCatalog.Message("ReaderIsStarting");
    private bool _isBusy;
    private bool _isReady;
    private ViewerPageIdentity? _currentPage;
    private bool _isWikiFavorite;
    private bool _isFavoriteLoading;
    private bool _canGoBack;
    private long _identityVersion;

    public ReaderPaneViewModel()
    {
        BackCommand = new AsyncCommand(_ => BackAsync(), _ => IsReady && !IsBusy && _canGoBack);
        ToggleFavoriteCommand = new AsyncCommand(_ => ToggleFavoriteAsync(), _ => CanFavorite);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Location
    {
        get => Text.Render(_location);
        private set => SetProperty(ref _location, value);
    }

    public string StatusMessage
    {
        get => Text.Render(_statusMessage);
        private set => SetProperty(ref _statusMessage, UiTextCatalog.FromSafeMessage(value));
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                BackCommand.NotifyCanExecuteChanged();
                ToggleFavoriteCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanFavorite));
            }
        }
    }

    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (SetProperty(ref _isReady, value))
            {
                BackCommand.NotifyCanExecuteChanged();
                ToggleFavoriteCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanFavorite));
            }
        }
    }

    public AsyncCommand BackCommand { get; }
    public AsyncCommand ToggleFavoriteCommand { get; }
    public bool CanFavorite => IsReady && !_isFavoriteLoading && _setFavorite is not null && _currentPage is not null;
    public bool CanGoBack => BackCommand.CanExecute(null);

    public bool IsWikiFavorite
    {
        get => _isWikiFavorite;
        private set
        {
            if (SetProperty(ref _isWikiFavorite, value))
                OnPropertyChanged(nameof(FavoriteActionText));
        }
    }

    public string FavoriteActionText => Text.Render(IsWikiFavorite ? UiTextCatalog.Message("UnfavoriteTheCurrentWikiPage") : UiTextCatalog.Message("FavoriteTheCurrentWikiPage"));

    public void Attach(
        Func<string, CancellationToken, Task> openPage,
        Func<CancellationToken, Task> goBack,
        bool canGoBack = false)
    {
        _openPage = openPage ?? throw new ArgumentNullException(nameof(openPage));
        _goBack = goBack ?? throw new ArgumentNullException(nameof(goBack));
        _canGoBack = canGoBack;
        IsReady = true;
        StatusMessage = _currentPage is null ? UiTextCatalog.Message("LocalViewerReady") : UiTextCatalog.Message("Opened3", _currentPage.CanonicalTitle);
        Location = _currentPage?.WikiDisplayTitle ?? UiTextCatalog.Message("WaitingForThePageToCommit");
    }

    public void Detach()
    {
        _openPage = null;
        _goBack = null;
        _canGoBack = false;
        _isFavoriteLoading = false;
        IsReady = false;
        IsBusy = false;
        StatusMessage = UiTextCatalog.Message("ReaderUnavailable");
        Location = UiTextCatalog.Message("WaitingForTheLocalViewer");
        Interlocked.Increment(ref _identityVersion);
        _currentPage = null;
        IsWikiFavorite = false;
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFavorite));
    }

    public void AttachFavorites(
        Func<ViewerPageIdentity, Task<bool>> isFavorite,
        Func<ViewerPageIdentity, bool, Task<ReaderFavoriteUpdate>> setFavorite)
    {
        _isFavorite = isFavorite ?? throw new ArgumentNullException(nameof(isFavorite));
        _setFavorite = setFavorite ?? throw new ArgumentNullException(nameof(setFavorite));
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFavorite));
    }

    public void DetachFavorites()
    {
        Interlocked.Increment(ref _identityVersion);
        _isFavorite = null;
        _setFavorite = null;
        IsWikiFavorite = false;
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFavorite));
    }

    public void SetStatus(string message) => StatusMessage = message;

    public void SetCanGoBack(bool canGoBack)
    {
        _canGoBack = canGoBack;
        BackCommand.NotifyCanExecuteChanged();
    }

    public void SetTransportReady(bool isReady)
    {
        IsReady = isReady && _openPage is not null;
        StatusMessage = IsReady
            ? UiTextCatalog.Message("LocalViewerReady")
            : UiTextCatalog.Message("PreparingLocalWikiContent");
    }

    public async Task ApplyPageIdentityAsync(ViewerPageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.CanonicalTitle))
            throw new ArgumentException("A canonical page title is required.", nameof(identity));
        if (_currentPage is { } current
            && (identity.Generation < current.Generation
                || identity.Generation == current.Generation
                && identity.NavigationVersion <= current.NavigationVersion))
            return;
        var version = Interlocked.Increment(ref _identityVersion);
        _currentPage = identity;
        Title = identity.CanonicalTitle;
        Location = identity.WikiDisplayTitle;
        _isFavoriteLoading = true;
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFavorite));
        try
        {
            var favorite = _isFavorite is not null && await _isFavorite(identity);
            if (version != Volatile.Read(ref _identityVersion) || _currentPage != identity)
                return;
            IsWikiFavorite = favorite;
            StatusMessage = UiTextCatalog.Message("Opened3", identity.CanonicalTitle);
        }
        catch
        {
            if (version != Volatile.Read(ref _identityVersion) || _currentPage != identity)
                return;
            IsWikiFavorite = false;
            StatusMessage = UiTextCatalog.Message("CouldNotReadTheWikiFavoriteStatus");
        }
        finally
        {
            if (version == Volatile.Read(ref _identityVersion) && _currentPage == identity)
            {
                _isFavoriteLoading = false;
                ToggleFavoriteCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanFavorite));
            }
        }
    }

    public async Task OpenAsync(ReaderPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_openPage is null)
        {
            StatusMessage = UiTextCatalog.Message("ReaderIsNotReadyYet");
            return;
        }

        IsBusy = true;
        var identityVersion = Volatile.Read(ref _identityVersion);
        StatusMessage = UiTextCatalog.Message("Opening", request.Title);
        try
        {
            await _openPage(request.Target, cancellationToken);
            if (identityVersion == Volatile.Read(ref _identityVersion))
                StatusMessage = UiTextCatalog.Message("RequestedOpening", request.Title);
        }
        catch (OperationCanceledException)
        {
            if (identityVersion == Volatile.Read(ref _identityVersion))
                StatusMessage = UiTextCatalog.Message("OpeningThePageWasCancelled");
        }
        catch
        {
            if (identityVersion == Volatile.Read(ref _identityVersion))
                StatusMessage = UiTextCatalog.Message("CouldNotOpenThisWikiPage");
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task BackAsync()
    {
        if (_goBack is null)
            return;
        IsBusy = true;
        var identityVersion = Volatile.Read(ref _identityVersion);
        try
        {
            await _goBack(CancellationToken.None);
            if (identityVersion == Volatile.Read(ref _identityVersion))
                StatusMessage = UiTextCatalog.Message("RequestedGoingBackToThePreviousPage");
        }
        catch (OperationCanceledException)
        {
            if (identityVersion == Volatile.Read(ref _identityVersion))
                StatusMessage = UiTextCatalog.Message("GoingBackWasCancelled");
        }
        catch
        {
            if (identityVersion == Volatile.Read(ref _identityVersion))
                StatusMessage = UiTextCatalog.Message("CouldNotGoBackToThePreviousPage");
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task ToggleFavoriteAsync()
    {
        if (_setFavorite is null || _currentPage is null || !CanFavorite)
            return;
        var identity = _currentPage;
        var version = Volatile.Read(ref _identityVersion);
        var requested = !IsWikiFavorite;
        ReaderFavoriteUpdate result;
        try
        {
            result = await _setFavorite(identity, requested);
        }
        catch
        {
            if (version == Volatile.Read(ref _identityVersion) && _currentPage == identity)
                StatusMessage = UiTextCatalog.Message("CouldNotUpdateWikiFavoritesCurrentStateIs");
            return;
        }
        if (version != Volatile.Read(ref _identityVersion) || _currentPage != identity)
            return;
        if (!result.Succeeded)
        {
            StatusMessage = UiTextCatalog.Message("CouldNotUpdateWikiFavoritesCurrentStateIs");
            return;
        }
        IsWikiFavorite = result.IsFavorite;
        StatusMessage = requested ? UiTextCatalog.Message("Favorited2", identity.CanonicalTitle) : UiTextCatalog.Message("RemovedFromFavorites2", identity.CanonicalTitle);
    }
}
