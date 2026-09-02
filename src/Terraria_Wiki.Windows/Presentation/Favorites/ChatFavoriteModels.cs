using Terraria_Wiki.Windows.AI;

namespace Terraria_Wiki.Windows.Presentation.Favorites;

internal sealed record ChatFavoriteSource(
    string RequestedTitle,
    string ResolvedTitle,
    string? Anchor,
    DateTimeOffset? LastModified)
{
    public AiAgentSource ToAgentSource() => new()
    {
        RequestedTitle = RequestedTitle,
        ResolvedTitle = ResolvedTitle,
        Anchor = Anchor,
        LastModified = LastModified,
    };

    public static ChatFavoriteSource FromAgentSource(AiAgentSource source) => new(
        source.RequestedTitle,
        source.ResolvedTitle,
        source.Anchor,
        source.LastModified);
}

internal sealed record ChatFavoriteRecord(
    Guid Id,
    string Question,
    string Markdown,
    IReadOnlyList<ChatFavoriteSource> Sources,
    DateTimeOffset FavoritedAt);

internal interface IChatFavoriteStore
{
    Task<IReadOnlyList<ChatFavoriteRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ChatFavoriteRecord favorite, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class MemoryChatFavoriteStore : IChatFavoriteStore
{
    private readonly Dictionary<Guid, ChatFavoriteRecord> _items = [];

    public Task<IReadOnlyList<ChatFavoriteRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ChatFavoriteRecord>>(
            _items.Values.OrderByDescending(item => item.FavoritedAt).ToArray());
    }

    public Task SaveAsync(ChatFavoriteRecord favorite, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items[favorite.Id] = favorite;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items.Remove(id);
        return Task.CompletedTask;
    }
}
