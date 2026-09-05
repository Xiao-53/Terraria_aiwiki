using System.Collections.ObjectModel;
using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

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
    private readonly Func<string, Task> _open;
    private readonly Func<string, Task> _remove;

    public WikiFavoriteItemViewModel(
        WikiFavoriteItem item,
        Func<string, Task> open,
        Func<string, Task> remove)
    {
        Item = item;
        _open = open;
        _remove = remove;
        OpenCommand = new AsyncCommand(_ => _open(Title));
        RemoveCommand = new AsyncCommand(_ => _remove(Title));
    }

    public WikiFavoriteItem Item { get; }
    public string Title => Item.WikiTitle;
    public string TimeText => Item.FavoritedAt.ToString("yyyy-MM-dd HH:mm");
    public AsyncCommand OpenCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    internal Task OpenAsync() => _open(Title);
}

internal sealed class FavoritesViewModel : ObservableObject
{
    private readonly IChatConversationStore _conversationStore;
    private readonly Func<ChatConversation, Task> _openChat;
    private readonly Func<string, Task> _openWiki;
    private WikiLibraryUseCases? _wikiLibrary;
    private string _statusMessage = "收藏会保存在本机。";
    private bool _isBusy;

    public FavoritesViewModel(
        IChatConversationStore conversationStore,
        Func<ChatConversation, Task> openChat,
        Func<string, Task> openWiki)
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

    public void AttachWikiLibrary(WikiLibraryUseCases library) =>
        _wikiLibrary = library ?? throw new ArgumentNullException(nameof(library));

    public void DetachWikiLibrary()
    {
        _wikiLibrary = null;
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

    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            var conversations = await _conversationStore.GetAllAsync();
            ChatFavorites.Clear();
            foreach (var item in conversations.Where(item => item.IsFavorite))
                ChatFavorites.Add(new ChatFavoriteItemViewModel(item, _openChat, RemoveChatAsync));

            WikiFavorites.Clear();
            if (_wikiLibrary is not null)
            {
                var favorites = await _wikiLibrary.GetFavoritesAsync(new PageRequest(0, 200));
                foreach (var item in favorites.Items)
                    WikiFavorites.Add(new WikiFavoriteItemViewModel(item, _openWiki, RemoveWikiAsync));
            }
            StatusMessage = IsEmpty ? "还没有收藏。" : $"{ChatFavorites.Count} 个对话 · {WikiFavorites.Count} 个 Wiki 页面";
        }
        catch
        {
            StatusMessage = "收藏暂时不可用。";
        }
        finally
        {
            IsBusy = false;
            RaiseCollectionProperties();
        }
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

    private async Task RemoveWikiAsync(string title) => await SetWikiFavoriteAsync(title, favorite: false);

    private void RaiseCollectionProperties()
    {
        OnPropertyChanged(nameof(HasChatFavorites));
        OnPropertyChanged(nameof(HasWikiFavorites));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
