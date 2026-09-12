using System.Collections.ObjectModel;
using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Terraria_Wiki.Windows.Presentation.Reader;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows.Presentation.Favorites;

internal sealed class ChatFavoriteItemViewModel
{
    private readonly Func<ChatConversation, Task> _open;
    private readonly Func<ChatConversation, Task> _remove;

    public ChatFavoriteItemViewModel(
        ChatConversation conversation,
        Func<ChatConversation, Task> open,
        Func<ChatConversation, Task> remove)
    {
        Conversation = conversation;
        _open = open;
        _remove = remove;
        OpenCommand = new AsyncCommand(_ => _open(Conversation));
        RemoveCommand = new AsyncCommand(_ => _remove(Conversation));
    }

    public ChatConversation Conversation { get; }
    public string Question => Conversation.Title;
    public string Preview => Conversation.Turns.LastOrDefault()?.Markdown ?? "";
    public string TimeText => $"{Conversation.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {Conversation.Turns.Count} 轮";
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    internal Task OpenAsync() => _open(Conversation);
    internal Task RemoveAsync() => _remove(Conversation);
}

internal sealed class WikiFavoriteItemViewModel
{
    private readonly Func<GlobalWikiFavoriteItem, Task> _open;
    private readonly Func<GlobalWikiFavoriteItem, Task> _remove;

    public WikiFavoriteItemViewModel(
        GlobalWikiFavoriteItem item,
        Func<GlobalWikiFavoriteItem, Task> open,
        Func<GlobalWikiFavoriteItem, Task> remove)
    {
        Item = item;
        _open = open;
        _remove = remove;
        OpenCommand = new AsyncCommand(_ => _open(Item));
        RemoveCommand = new AsyncCommand(_ => _remove(Item));
    }

    public GlobalWikiFavoriteItem Item { get; }
    public string Title => Item.PageTitle;
    public string WikiTitle => Item.WikiDisplayTitle;
    public string TimeText => Item.FavoritedAt.ToString("yyyy-MM-dd HH:mm");
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    internal Task OpenAsync() => _open(Item);
}

internal sealed class FavoritesViewModel : ObservableObject
{
    private readonly IChatConversationStore _conversationStore;
    private readonly Func<ChatConversation, Task> _openChat;
    private readonly Func<GlobalWikiFavoriteItem, Task> _openWiki;
    private readonly object _refreshGate = new();
    private WikiLibraryUseCases? _wikiLibrary;
    private Task? _refreshTask;
    private bool _refreshRequested;
    private int _attachmentGeneration;
    private string _statusMessage = "收藏会保存在本机。";
    private bool _isBusy;

    public FavoritesViewModel(
        IChatConversationStore conversationStore,
        Func<ChatConversation, Task> openChat,
        Func<GlobalWikiFavoriteItem, Task> openWiki)
    {
        _conversationStore = conversationStore ?? throw new ArgumentNullException(nameof(conversationStore));
        _openChat = openChat ?? throw new ArgumentNullException(nameof(openChat));
        _openWiki = openWiki ?? throw new ArgumentNullException(nameof(openWiki));
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => !IsBusy);
    }

    public ObservableCollection<ChatFavoriteItemViewModel> ChatFavorites { get; } = [];
    public ObservableCollection<WikiFavoriteItemViewModel> WikiFavorites { get; } = [];
    public event Action<Guid, bool>? ConversationFavoriteChanged;
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) RefreshCommand.NotifyCanExecuteChanged(); }
    }
    public bool HasChatFavorites => ChatFavorites.Count > 0;
    public bool HasWikiFavorites => WikiFavorites.Count > 0;
    public bool IsEmpty => !HasChatFavorites && !HasWikiFavorites;
    public AsyncCommand RefreshCommand { get; }

    public void AttachWikiLibrary(WikiLibraryUseCases library)
    {
        _wikiLibrary = library ?? throw new ArgumentNullException(nameof(library));
        Interlocked.Increment(ref _attachmentGeneration);
    }

    public void DetachWikiLibrary()
    {
        _wikiLibrary = null;
        Interlocked.Increment(ref _attachmentGeneration);
        WikiFavorites.Clear();
        RaiseCollectionProperties();
    }

    public async Task<bool> IsWikiFavoriteAsync(string title)
    {
        if (_wikiLibrary is null || string.IsNullOrWhiteSpace(title))
            return false;
        try { return await _wikiLibrary.IsFavoriteAsync(title); }
        catch { return false; }
    }

    public async Task<bool> IsWikiFavoriteAsync(ViewerPageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var library = _wikiLibrary;
        if (library is null)
            return false;
        return await library.IsFavoriteAsync(identity.WikiId, identity.CanonicalTitle);
    }

    public async Task<ReaderFavoriteUpdate> SetWikiFavoriteAsync(ViewerPageIdentity identity, bool favorite)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return await SetWikiFavoriteCoreAsync(
            identity.WikiId,
            identity.WikiDisplayTitle,
            identity.CanonicalTitle,
            favorite);
    }

    private async Task<ReaderFavoriteUpdate> SetWikiFavoriteCoreAsync(
        int wikiId,
        string wikiDisplayTitle,
        string pageTitle,
        bool favorite)
    {
        var library = _wikiLibrary;
        if (library is null)
            return new ReaderFavoriteUpdate(false, IsFavorite: false);
        try
        {
            var state = await library.SetFavoriteAsync(wikiId, pageTitle, favorite);
            await RefreshAsync();
            StatusMessage = favorite
                ? $"已收藏 {wikiDisplayTitle} · {pageTitle}。"
                : $"已取消收藏 {wikiDisplayTitle} · {pageTitle}。";
            return new ReaderFavoriteUpdate(true, state);
        }
        catch
        {
            StatusMessage = "无法更新 Wiki 收藏；现有收藏未被更改。";
            return new ReaderFavoriteUpdate(false, IsFavorite: !favorite);
        }
    }

    public async Task<bool> SetWikiFavoriteAsync(string title, bool favorite)
    {
        if (_wikiLibrary is null || string.IsNullOrWhiteSpace(title))
            return false;
        try
        {
            if (favorite)
                await _wikiLibrary.AddFavoriteAsync(title);
            else
                await _wikiLibrary.RemoveFavoriteAsync(title);
            await RefreshAsync();
            StatusMessage = favorite ? $"已收藏 {title}。" : $"已取消收藏 {title}。";
            return favorite;
        }
        catch
        {
            StatusMessage = "无法更新 Wiki 收藏。";
            return !favorite;
        }
    }

    public Task RefreshAsync()
    {
        lock (_refreshGate)
        {
            _refreshRequested = true;
            return _refreshTask ??= RefreshLoopAsync();
        }
    }

    private async Task RefreshLoopAsync()
    {
        await Task.Yield();
        IsBusy = true;
        try
        {
            while (true)
            {
                lock (_refreshGate)
                    _refreshRequested = false;
                await RefreshOnceAsync();
                lock (_refreshGate)
                {
                    if (_refreshRequested)
                        continue;
                    _refreshTask = null;
                    break;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshOnceAsync()
    {
        var generation = Volatile.Read(ref _attachmentGeneration);
        var library = _wikiLibrary;
        IReadOnlyList<ChatConversation>? conversations = null;
        GlobalWikiFavoritesResult? wikiResult = null;
        var chatFailed = false;
        var wikiFailed = false;
        try { conversations = await _conversationStore.GetAllAsync(); }
        catch { chatFailed = true; }
        if (library is not null)
        {
            try { wikiResult = await library.GetAllFavoritesAsync(); }
            catch { wikiFailed = true; }
        }
        if (generation != Volatile.Read(ref _attachmentGeneration) || library != _wikiLibrary)
            return;

        if (!chatFailed && conversations is not null)
        {
            ChatFavorites.Clear();
            foreach (var item in conversations.Where(item => item.IsFavorite))
                ChatFavorites.Add(new ChatFavoriteItemViewModel(item, _openChat, RemoveChatAsync));
        }
        if (!wikiFailed && wikiResult is not null)
        {
            WikiFavorites.Clear();
            foreach (var item in wikiResult.Items)
                WikiFavorites.Add(new WikiFavoriteItemViewModel(item, OpenWikiAsync, RemoveWikiAsync));
        }

        RaiseCollectionProperties();
        StatusMessage = chatFailed || wikiFailed
            ? "部分收藏暂时不可用；已保留仍可读取的内容。"
            : wikiResult?.Issues.Count > 0
                ? $"{ChatFavorites.Count} 个对话 · {WikiFavorites.Count} 个 Wiki 页面 · {wikiResult.Issues.Count} 个 Wiki 暂不可用"
                : IsEmpty ? "还没有收藏。" : $"{ChatFavorites.Count} 个对话 · {WikiFavorites.Count} 个 Wiki 页面";
    }

    private async Task RemoveChatAsync(ChatConversation conversation)
    {
        try
        {
            await _conversationStore.SaveAsync(conversation with
            {
                IsFavorite = false,
                UpdatedAt = DateTimeOffset.Now,
            });
            ConversationFavoriteChanged?.Invoke(conversation.Id, false);
            await RefreshAsync();
            StatusMessage = "已取消收藏；对话仍保留在历史中。";
        }
        catch
        {
            StatusMessage = "无法更新对话收藏；现有收藏未被更改。";
        }
    }

    private async Task RemoveWikiAsync(GlobalWikiFavoriteItem item)
    {
        var result = await SetWikiFavoriteCoreAsync(
            item.WikiId,
            item.WikiDisplayTitle,
            item.PageTitle,
            favorite: false);
        if (!result.Succeeded)
            StatusMessage = "无法取消此 Wiki 收藏；现有收藏未被更改。";
    }

    private async Task OpenWikiAsync(GlobalWikiFavoriteItem item)
    {
        try
        {
            await _openWiki(item);
            StatusMessage = $"已打开 {item.WikiDisplayTitle} · {item.PageTitle}。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "打开 Wiki 收藏已取消。";
        }
        catch
        {
            StatusMessage = "无法切换并打开此 Wiki 收藏；现有收藏未被更改。";
        }
    }

    private void RaiseCollectionProperties()
    {
        OnPropertyChanged(nameof(HasChatFavorites));
        OnPropertyChanged(nameof(HasWikiFavorites));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
