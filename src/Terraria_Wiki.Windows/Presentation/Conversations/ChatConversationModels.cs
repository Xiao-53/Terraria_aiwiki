using Terraria_Wiki.Windows.AI;

namespace Terraria_Wiki.Windows.Presentation.Conversations;

internal enum ChatConversationTurnStatus
{
    Pending,
    Completed,
    InsufficientEvidence,
    Cancelled,
    Failed,
    Interrupted,
}

internal sealed record ChatConversationTurn
{
    public Guid Id { get; init; }
    public string Question { get; init; } = string.Empty;
    public string Markdown { get; init; } = string.Empty;
    public IReadOnlyList<AiAgentSource> Sources { get; init; } = [];
    public IReadOnlyList<AiAgentToolCall> ToolTrace { get; init; } = [];
    public AiAgentUsage? Usage { get; init; }
    public ChatConversationTurnStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public bool IsModelHistoryEligible => Status is ChatConversationTurnStatus.Completed
        or ChatConversationTurnStatus.InsufficientEvidence;
}

internal sealed record ChatConversation
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsFavorite { get; init; }
    public IReadOnlyList<ChatConversationTurn> Turns { get; init; } = [];

    public static string CreateTitle(string question)
    {
        var normalized = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 60 ? normalized : normalized[..60] + "…";
    }
}

internal interface IChatConversationStore
{
    Task<IReadOnlyList<ChatConversation>> GetAllAsync(
        string? filter = null,
        CancellationToken cancellationToken = default);
    Task<ChatConversation?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(ChatConversation conversation, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteUnfavoritedAsync(CancellationToken cancellationToken = default);
}

internal sealed class MemoryChatConversationStore : IChatConversationStore
{
    private readonly Dictionary<Guid, ChatConversation> _items = [];
    private readonly object _gate = new();

    public Task<IReadOnlyList<ChatConversation>> GetAllAsync(
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IEnumerable<ChatConversation> values = _items.Values;
            if (!string.IsNullOrWhiteSpace(filter))
                values = values.Where(item => item.Title.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<IReadOnlyList<ChatConversation>>(
                values.OrderByDescending(item => item.UpdatedAt).ToArray());
        }
    }

    public Task<ChatConversation?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_items.GetValueOrDefault(id));
    }

    public Task SaveAsync(ChatConversation conversation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _items[conversation.Id] = conversation;
            Prune();
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            _items.Remove(id);
        return Task.CompletedTask;
    }

    public Task DeleteUnfavoritedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            foreach (var id in _items.Values.Where(item => !item.IsFavorite).Select(item => item.Id).ToArray())
                _items.Remove(id);
        }
        return Task.CompletedTask;
    }

    private void Prune()
    {
        foreach (var item in _items.Values.Where(item => !item.IsFavorite)
                     .OrderByDescending(item => item.UpdatedAt).Skip(100).ToArray())
            _items.Remove(item.Id);
    }
}
