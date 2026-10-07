using System.Collections.ObjectModel;
using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Library;

internal enum WikiLibrarySection
{
    Pages,
    Conversations,
}

internal sealed class WikiPageItemViewModel
{
    public WikiPageItemViewModel(WikiPageListItem item, Func<string, Task> open)
    {
        Item = item;
        _open = open;
        OpenCommand = new AsyncCommand(_ => open(Title));
    }

    public WikiPageListItem Item { get; }
    public string Title => Item.Title;
    public string ModifiedText => Item.LastModified.ToString("yyyy-MM-dd HH:mm");
    public AsyncCommand OpenCommand { get; }
    internal Task OpenAsync() => _open(Title);

    private readonly Func<string, Task> _open;
}

internal sealed class ConversationHistoryItemViewModel : ObservableObject
{
    public ConversationHistoryItemViewModel(
        ChatConversation conversation,
        Func<ChatConversation, Task> open,
        Action<ChatConversation> requestDelete)
    {
        Conversation = conversation;
        _open = open;
        OpenCommand = new AsyncCommand(_ => open(Conversation));
        DeleteCommand = new RelayCommand(_ => requestDelete(Conversation));
    }

    public ChatConversation Conversation { get; }
    public string Title => Conversation.Title;
    public string UpdatedText => Conversation.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Detail => Text.Render(UiTextCatalog.Join("", UiTextCatalog.Message("Turns2", Conversation.Turns.Count), Conversation.IsFavorite ? UiTextCatalog.Message("Favorited3") : ""));
    public AsyncCommand OpenCommand { get; }
    public RelayCommand DeleteCommand { get; }
    internal Task OpenAsync() => _open(Conversation);

    private readonly Func<ChatConversation, Task> _open;
}

internal sealed class WikiLibraryViewModel : ObservableObject, IDisposable
{
    protected override void OnTextCatalogChanged()
    {
        foreach (var item in History) item.UseTextCatalog(Text);
    }
    internal const int PageSize = 100;
    private static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(180);
    private readonly IChatConversationStore _conversationStore;
    private readonly Func<string, Task> _openPage;
    private readonly Func<ChatConversation, Task> _openConversation;
    private readonly Action<Guid> _conversationDeleted;
    private Func<PageRequest, CancellationToken, Task<PageSlice<WikiPageListItem>>>? _getPages;
    private CancellationTokenSource? _loadCancellation;
    private WikiLibrarySection _currentSection = WikiLibrarySection.Pages;
    private ChatConversation? _pendingDelete;
    private string _query = string.Empty;
    private string _statusMessage = UiTextCatalog.Message("PreparingTheWikiLibrary");
    private bool _isWikiAvailable;
    private bool _isBusy;
    private bool _isDeleteConfirmationVisible;
    private bool _isClearConfirmationVisible;
    private int _totalCount;
    private bool _disposed;

    public WikiLibraryViewModel(
        IChatConversationStore conversationStore,
        Func<string, Task> openPage,
        Func<ChatConversation, Task> openConversation,
        Action<Guid> conversationDeleted)
    {
        _conversationStore = conversationStore;
        _openPage = openPage;
        _openConversation = openConversation;
        _conversationDeleted = conversationDeleted;
        SelectPagesCommand = new AsyncCommand(_ => SelectSectionAsync(WikiLibrarySection.Pages));
        SelectHistoryCommand = new AsyncCommand(_ => SelectSectionAsync(WikiLibrarySection.Conversations));
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => !IsBusy);
        LoadMoreCommand = new AsyncCommand(_ => LoadMoreAsync(), _ => CanLoadMore);
        RequestClearHistoryCommand = new RelayCommand(_ => RequestClearHistory(), _ => CanRequestClearHistory);
        CancelClearHistoryCommand = new RelayCommand(_ => CancelConfirmations());
        ConfirmClearHistoryCommand = new AsyncCommand(_ => ConfirmClearHistoryAsync(), _ => IsClearConfirmationVisible && !IsBusy);
        CancelDeleteCommand = new RelayCommand(_ => CancelConfirmations());
        ConfirmDeleteCommand = new AsyncCommand(_ => ConfirmDeleteAsync(), _ => IsDeleteConfirmationVisible && !IsBusy);
    }

    public ObservableCollection<WikiPageItemViewModel> Pages { get; } = [];
    public ObservableCollection<ConversationHistoryItemViewModel> History { get; } = [];
    public WikiLibrarySection CurrentSection
    {
        get => _currentSection;
        private set
        {
            if (!SetProperty(ref _currentSection, value))
                return;
            OnPropertyChanged(nameof(IsPagesSelected));
            OnPropertyChanged(nameof(IsHistorySelected));
            OnPropertyChanged(nameof(IsPagesVisible));
            OnPropertyChanged(nameof(IsHistoryVisible));
            RefreshViewState();
        }
    }
    public string Query
    {
        get => _query;
        set { if (SetProperty(ref _query, value ?? string.Empty)) _ = LoadAsync(true, true); }
    }
    public string StatusMessage { get => Text.Render(_statusMessage); private set => SetProperty(ref _statusMessage, UiTextCatalog.FromSafeMessage(value)); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) RefreshViewState(); }
    }
    public bool IsClearConfirmationVisible
    {
        get => _isClearConfirmationVisible;
        private set { if (SetProperty(ref _isClearConfirmationVisible, value)) RefreshViewState(); }
    }
    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        private set { if (SetProperty(ref _isDeleteConfirmationVisible, value)) RefreshViewState(); }
    }
    public string DeleteConfirmationText => Text.Render(_pendingDelete is null
        ? string.Empty
        : UiTextCatalog.Message("DeleteConversation", _pendingDelete.Title));
    public int TotalCount
    {
        get => _totalCount;
        private set { if (SetProperty(ref _totalCount, value)) RefreshViewState(); }
    }
    public bool IsPagesSelected => CurrentSection == WikiLibrarySection.Pages;
    public bool IsHistorySelected => CurrentSection == WikiLibrarySection.Conversations;
    public bool IsPagesVisible => IsPagesSelected;
    public bool IsHistoryVisible => IsHistorySelected;
    public bool HasItems => CurrentItemCount > 0;
    public bool IsEmpty => !IsBusy && CurrentItemCount == 0;
    public bool CanLoadMore => IsPagesSelected && _isWikiAvailable && !IsBusy && Pages.Count < TotalCount;
    public bool CanRequestClearHistory => IsHistorySelected && !IsBusy && History.Any(item => !item.Conversation.IsFavorite);
    public AsyncCommand SelectPagesCommand { get; }
    public AsyncCommand SelectHistoryCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand LoadMoreCommand { get; }
    public RelayCommand RequestClearHistoryCommand { get; }
    public RelayCommand CancelClearHistoryCommand { get; }
    public AsyncCommand ConfirmClearHistoryCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }
    public AsyncCommand ConfirmDeleteCommand { get; }
    private int CurrentItemCount => IsPagesSelected ? Pages.Count : History.Count;

    public void Attach(WikiLibraryUseCases library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _getPages = library.GetPagesAsync;
        _isWikiAvailable = true;
        StatusMessage = UiTextCatalog.Message("BrowseAllPagesOrLocalConversationHistory");
    }

    internal void Attach(Func<PageRequest, CancellationToken, Task<PageSlice<WikiPageListItem>>> getPages)
    {
        _getPages = getPages;
        _isWikiAvailable = true;
    }

    public void Detach()
    {
        CancelCurrentLoad();
        _getPages = null;
        _isWikiAvailable = false;
        Pages.Clear();
        if (IsPagesSelected)
            StatusMessage = UiTextCatalog.Message("TheLocalWikiDataSourceIsUnavailable");
        RefreshViewState();
    }

    internal async Task SelectSectionAsync(WikiLibrarySection section)
    {
        if (CurrentSection == section)
            return;
        CurrentSection = section;
        CancelConfirmations();
        await LoadAsync(true, false);
    }

    internal Task RefreshAsync() => LoadAsync(true, false);
    internal Task LoadMoreAsync() => CanLoadMore ? LoadAsync(false, false) : Task.CompletedTask;

    internal void RequestClearHistory()
    {
        if (CanRequestClearHistory)
            IsClearConfirmationVisible = true;
    }

    internal void CancelClearHistory() => CancelConfirmations();

    internal async Task ConfirmClearHistoryAsync()
    {
        if (!IsClearConfirmationVisible || IsBusy)
            return;
        IsBusy = true;
        string resultMessage;
        try
        {
            var removedIds = (await _conversationStore.GetAllAsync())
                .Where(item => !item.IsFavorite)
                .Select(item => item.Id)
                .ToArray();
            await _conversationStore.DeleteUnfavoritedAsync();
            foreach (var id in removedIds)
                _conversationDeleted(id);
            resultMessage = UiTextCatalog.Message("UnfavoritedConversationHistoryCleared");
        }
        catch { resultMessage = UiTextCatalog.Message("CouldNotClearConversationHistoryExistingRecordsAre"); }
        finally { IsBusy = false; CancelConfirmations(); }
        await LoadAsync(true, false);
        StatusMessage = resultMessage;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Detach();
    }

    private async Task<bool> LoadAsync(bool reset, bool useFilterDelay)
    {
        if (IsPagesSelected && (!_isWikiAvailable || _getPages is null))
            return false;
        CancelCurrentLoad();
        var current = new CancellationTokenSource();
        _loadCancellation = current;
        var token = current.Token;
        var section = CurrentSection;
        var query = Query.Trim();
        var offset = reset ? 0 : CurrentItemCount;
        IsBusy = true;
        StatusMessage = section == WikiLibrarySection.Pages ? UiTextCatalog.Message("LoadingAllPages") : UiTextCatalog.Message("LoadingConversationHistory");
        try
        {
            if (useFilterDelay)
                await Task.Delay(FilterDelay, token);
            if (section == WikiLibrarySection.Pages)
            {
                var slice = await _getPages!(new PageRequest(offset, PageSize, query), token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(current, _loadCancellation) || CurrentSection != section)
                    return false;
                if (reset) Pages.Clear();
                foreach (var item in slice.Items) Pages.Add(new WikiPageItemViewModel(item, _openPage));
                TotalCount = slice.TotalCount;
                StatusMessage = TotalCount == 0 ? EmptyMessage(UiTextCatalog.Message("Pages")) : UiTextCatalog.Message("ShowingPages", Pages.Count, TotalCount);
            }
            else
            {
                var conversations = await _conversationStore.GetAllAsync(query, token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(current, _loadCancellation) || CurrentSection != section)
                    return false;
                History.Clear();
                foreach (var item in conversations)
                    History.Add(new ConversationHistoryItemViewModel(item, _openConversation, RequestDelete).WithText(Text));
                TotalCount = History.Count;
                StatusMessage = TotalCount == 0 ? EmptyMessage(UiTextCatalog.Message("Conversations")) : UiTextCatalog.Message("LocalConversations", TotalCount);
            }
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch
        {
            if (ReferenceEquals(current, _loadCancellation))
                StatusMessage = section == WikiLibrarySection.Pages
                    ? UiTextCatalog.Message("TheLibraryIsTemporarilyUnavailableLocalDataIs")
                    : UiTextCatalog.Message("ConversationHistoryIsTemporarilyUnavailableTheCurrentConversation");
            return false;
        }
        finally
        {
            if (ReferenceEquals(current, _loadCancellation))
            {
                _loadCancellation = null;
                current.Dispose();
                IsBusy = false;
                RefreshViewState();
            }
        }
    }

    private void RequestDelete(ChatConversation conversation)
    {
        _pendingDelete = conversation;
        OnPropertyChanged(nameof(DeleteConfirmationText));
        IsDeleteConfirmationVisible = true;
    }

    internal async Task ConfirmDeleteAsync()
    {
        var conversation = _pendingDelete;
        if (conversation is null || !IsDeleteConfirmationVisible || IsBusy)
            return;
        IsBusy = true;
        string resultMessage;
        try
        {
            await _conversationStore.DeleteAsync(conversation.Id);
            _conversationDeleted(conversation.Id);
            resultMessage = UiTextCatalog.Message("ConversationDeleted");
        }
        catch { resultMessage = UiTextCatalog.Message("CouldNotDeleteTheConversationExistingRecordsAre"); }
        finally { IsBusy = false; CancelConfirmations(); }
        await LoadAsync(true, false);
        StatusMessage = resultMessage;
    }

    private void CancelConfirmations()
    {
        _pendingDelete = null;
        IsDeleteConfirmationVisible = false;
        IsClearConfirmationVisible = false;
        OnPropertyChanged(nameof(DeleteConfirmationText));
    }

    private string EmptyMessage(string subject) => Query.Trim().Length == 0
        ? UiTextCatalog.Message("NoYet", subject)
        : UiTextCatalog.Message("NoMatching", Query.Trim(), subject);

    private void CancelCurrentLoad()
    {
        var current = _loadCancellation;
        _loadCancellation = null;
        if (current is null) return;
        current.Cancel();
        current.Dispose();
    }

    private void RefreshViewState()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(CanRequestClearHistory));
        RefreshCommand.NotifyCanExecuteChanged();
        LoadMoreCommand.NotifyCanExecuteChanged();
        RequestClearHistoryCommand.NotifyCanExecuteChanged();
        ConfirmClearHistoryCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();
    }
}
