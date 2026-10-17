using System.Text.Json;
using System.Text.Json.Serialization;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Favorites;

namespace Terraria_Wiki.Windows.Hosting;

internal sealed class ChatConversationStore : IChatConversationStore
{
    private const int CurrentVersion = 1;
    private const int MaxUnfavorited = 100;
    private const int MaxFavorites = 200;
    private const int MaxTurns = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string? _legacyFavoritesPath;

    public ChatConversationStore(string directoryPath, string? legacyFavoritesPath = null)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Path.IsPathFullyQualified(directoryPath))
            throw new ArgumentException("An absolute conversations directory is required.", nameof(directoryPath));
        DirectoryPath = Path.GetFullPath(directoryPath);
        IndexPath = Path.Combine(DirectoryPath, "index.json");
        _legacyFavoritesPath = legacyFavoritesPath is null ? null : Path.GetFullPath(legacyFavoritesPath);
    }

    public string DirectoryPath { get; }
    internal string IndexPath { get; }
    internal string IndexBackupPath => IndexPath + ".backup";

    public static ChatConversationStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new ChatConversationStoreException("conversation-path-unavailable", "对话历史目录不可用。");
        var root = Path.Combine(localAppData, "Terraria AI Wiki");
        return new ChatConversationStore(
            Path.Combine(root, "conversations"),
            Path.Combine(root, "chat-favorites.json"));
    }

    public async Task<IReadOnlyList<ChatConversation>> GetAllAsync(
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadOrMigrateIndexAsync(cancellationToken).ConfigureAwait(false);
            var result = new List<ChatConversation>();
            foreach (var item in index.Items.OrderByDescending(item => item.UpdatedAt))
            {
                if (!string.IsNullOrWhiteSpace(filter)
                    && !item.Title.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                var conversation = await TryLoadConversationAsync(item.Id, cancellationToken).ConfigureAwait(false);
                if (conversation is not null)
                    result.Add(await NormalizePendingAsync(conversation, cancellationToken).ConfigureAwait(false));
            }
            if (result.Any(item => index.Items.First(entry => entry.Id == item.Id).UpdatedAt != item.UpdatedAt))
            {
                index = new ChatConversationIndex(
                    CurrentVersion,
                    index.Items.Select(item => result.FirstOrDefault(value => value.Id == item.Id) is { } value
                        ? ToIndexItem(value)
                        : item).OrderByDescending(item => item.UpdatedAt).ToArray());
                await WriteJsonAsync(IndexPath, index, cancellationToken).ConfigureAwait(false);
            }
            return result.OrderByDescending(item => item.UpdatedAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ChatConversation?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            return null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadOrMigrateIndexAsync(cancellationToken).ConfigureAwait(false);
            if (index.Items.All(item => item.Id != id))
                return null;
            var conversation = await TryLoadConversationAsync(id, cancellationToken).ConfigureAwait(false);
            if (conversation is null)
                return null;
            var normalized = await NormalizePendingAsync(conversation, cancellationToken).ConfigureAwait(false);
            if (normalized.UpdatedAt != conversation.UpdatedAt)
            {
                var items = index.Items.Select(item => item.Id == id ? ToIndexItem(normalized) : item)
                    .OrderByDescending(item => item.UpdatedAt).ToArray();
                await WriteJsonAsync(IndexPath, new ChatConversationIndex(CurrentVersion, items), cancellationToken)
                    .ConfigureAwait(false);
            }
            return normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ChatConversation conversation,
        CancellationToken cancellationToken = default)
    {
        Validate(conversation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadOrMigrateIndexAsync(cancellationToken).ConfigureAwait(false);
            var existing = index.Items.FirstOrDefault(item => item.Id == conversation.Id);
            if (conversation.IsFavorite && existing is not { IsFavorite: true }
                && index.Items.Count(item => item.IsFavorite) >= MaxFavorites)
                throw new ChatConversationStoreException("conversation-favorite-limit", "收藏对话已达到 200 个，请先取消部分收藏。");

            await WriteJsonAsync(ConversationPath(conversation.Id), conversation, cancellationToken)
                .ConfigureAwait(false);
            var items = index.Items.Where(item => item.Id != conversation.Id)
                .Append(ToIndexItem(conversation))
                .ToList();
            var expired = items.Where(item => !item.IsFavorite)
                .OrderByDescending(item => item.UpdatedAt)
                .Skip(MaxUnfavorited)
                .ToArray();
            foreach (var item in expired)
                items.Remove(item);
            await WriteJsonAsync(
                IndexPath,
                new ChatConversationIndex(CurrentVersion, items.OrderByDescending(item => item.UpdatedAt).ToArray()),
                cancellationToken).ConfigureAwait(false);
            foreach (var item in expired)
                TryDeleteConversationFiles(item.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadOrMigrateIndexAsync(cancellationToken).ConfigureAwait(false);
            var items = index.Items.Where(item => item.Id != id).ToArray();
            if (items.Length == index.Items.Count)
                return;
            await WriteJsonAsync(IndexPath, new ChatConversationIndex(CurrentVersion, items), cancellationToken)
                .ConfigureAwait(false);
            TryDeleteConversationFiles(id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteUnfavoritedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadOrMigrateIndexAsync(cancellationToken).ConfigureAwait(false);
            var removed = index.Items.Where(item => !item.IsFavorite).ToArray();
            var kept = index.Items.Where(item => item.IsFavorite).ToArray();
            await WriteJsonAsync(IndexPath, new ChatConversationIndex(CurrentVersion, kept), cancellationToken)
                .ConfigureAwait(false);
            foreach (var item in removed)
                TryDeleteConversationFiles(item.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ChatConversationIndex> LoadOrMigrateIndexAsync(CancellationToken cancellationToken)
    {
        var index = await TryReadAsync<ChatConversationIndex>(IndexPath, cancellationToken).ConfigureAwait(false)
                    ?? await TryReadAsync<ChatConversationIndex>(IndexBackupPath, cancellationToken).ConfigureAwait(false);
        if (index is not null && IsValidIndex(index))
            return index;
        if (File.Exists(IndexPath) || File.Exists(IndexBackupPath))
            throw new ChatConversationStoreException("conversation-index-invalid", "对话历史索引无法读取。");
        return await MigrateLegacyAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatConversationIndex> MigrateLegacyAsync(CancellationToken cancellationToken)
    {
        var items = new List<ChatConversationIndexItem>();
        if (_legacyFavoritesPath is not null && File.Exists(_legacyFavoritesPath))
        {
            var legacy = await TryReadAsync<ChatFavoriteDocument>(_legacyFavoritesPath, cancellationToken)
                .ConfigureAwait(false);
            if (legacy is not { Version: 1 }
                || legacy.Items is null
                || legacy.Items.Count > MaxFavorites
                || legacy.Items.Any(item => item is null)
                || legacy.Items.Select(item => item.Id).Distinct().Count() != legacy.Items.Count)
                throw new ChatConversationStoreException(
                    "conversation-legacy-invalid", "旧对话收藏无法迁移；原文件未被修改。");
            try
            {
                foreach (var favorite in legacy.Items.Take(MaxFavorites))
                {
                    var conversation = FromLegacy(favorite);
                    Validate(conversation);
                    await WriteJsonAsync(ConversationPath(conversation.Id), conversation, cancellationToken)
                        .ConfigureAwait(false);
                    items.Add(ToIndexItem(conversation));
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NullReferenceException)
            {
                throw new ChatConversationStoreException(
                    "conversation-legacy-invalid", "旧对话收藏无法迁移；原文件未被修改。", exception);
            }
        }
        var index = new ChatConversationIndex(CurrentVersion, items.OrderByDescending(item => item.UpdatedAt).ToArray());
        await WriteJsonAsync(IndexPath, index, cancellationToken).ConfigureAwait(false);
        return index;
    }

    private async Task<ChatConversation?> TryLoadConversationAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await TryReadAsync<ChatConversation>(ConversationPath(id), cancellationToken)
                           ?? await TryReadAsync<ChatConversation>(ConversationBackupPath(id), cancellationToken);
        if (conversation is null)
            return null;
        try
        {
            Validate(conversation);
            return conversation;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private async Task<ChatConversation> NormalizePendingAsync(
        ChatConversation conversation,
        CancellationToken cancellationToken)
    {
        if (conversation.Turns.All(turn => turn.Status != ChatConversationTurnStatus.Pending))
            return conversation;
        var now = DateTimeOffset.Now;
        var normalized = conversation with
        {
            UpdatedAt = now,
            Turns = conversation.Turns.Select(turn => turn.Status == ChatConversationTurnStatus.Pending
                ? turn with
                {
                    Status = ChatConversationTurnStatus.Interrupted,
                    Markdown = "上次回答中断。",
                    UpdatedAt = now,
                }
                : turn).ToArray(),
        };
        await WriteJsonAsync(ConversationPath(normalized.Id), normalized, cancellationToken).ConfigureAwait(false);
        return normalized;
    }

    private async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DirectoryPath);
        var tempPath = Path.Combine(DirectoryPath, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(tempPath, path, path + ".backup", ignoreMetadataErrors: true);
            else
                File.Move(tempPath, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ChatConversationStoreException("conversation-write-failed", "无法保存对话历史。", exception);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static async Task<T?> TryReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return default;
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or IOException
            or UnauthorizedAccessException or ArgumentException)
        {
            return default;
        }
    }

    private string ConversationPath(Guid id) => Path.Combine(DirectoryPath, id.ToString("D") + ".json");
    private string ConversationBackupPath(Guid id) => ConversationPath(id) + ".backup";

    private void TryDeleteConversationFiles(Guid id)
    {
        foreach (var path in new[] { ConversationPath(id), ConversationBackupPath(id) })
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static ChatConversation FromLegacy(ChatFavoriteRecord favorite) => new()
    {
        Id = favorite.Id,
        Title = ChatConversation.CreateTitle(favorite.Question),
        CreatedAt = favorite.FavoritedAt,
        UpdatedAt = favorite.FavoritedAt,
        IsFavorite = true,
        Turns =
        [
            new ChatConversationTurn
            {
                Id = Guid.NewGuid(),
                Question = favorite.Question,
                Markdown = favorite.Markdown,
                Sources = favorite.Sources.Select(source => source.ToAgentSource()).ToArray(),
                Status = ChatConversationTurnStatus.Completed,
                CreatedAt = favorite.FavoritedAt,
                UpdatedAt = favorite.FavoritedAt,
            },
        ],
    };

    private static ChatConversationIndexItem ToIndexItem(ChatConversation conversation) => new(
        conversation.Id,
        conversation.Title,
        conversation.CreatedAt,
        conversation.UpdatedAt,
        conversation.IsFavorite,
        conversation.Turns.Count);

    private static void Validate(ChatConversation conversation)
    {
        if (conversation.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(conversation.Title)
            || conversation.Title.Length > 61
            || conversation.Turns is null
            || conversation.Turns.Count is < 1 or > MaxTurns
            || conversation.CreatedAt == default
            || conversation.UpdatedAt < conversation.CreatedAt
            || conversation.Turns.Select(turn => turn?.Id).Distinct().Count() != conversation.Turns.Count
            || conversation.Turns.Any(IsInvalidTurn))
        {
            throw new ArgumentException("The chat conversation is invalid.", nameof(conversation));
        }
    }

    private static bool IsInvalidTurn(ChatConversationTurn? turn) => turn is null
        || turn.Id == Guid.Empty
        || string.IsNullOrWhiteSpace(turn.Question)
        || AiTextLimits.IsTooLong(turn.Question)
        || turn.Markdown is null
        || AiTextLimits.IsTooLong(turn.Markdown)
        || turn.Status is ChatConversationTurnStatus.Completed or ChatConversationTurnStatus.InsufficientEvidence
            && string.IsNullOrWhiteSpace(turn.Markdown)
        || turn.Sources is null
        || turn.Sources.Count > 32
        || turn.Sources.Any(source => source is null
            || string.IsNullOrWhiteSpace(source.RequestedTitle)
            || source.RequestedTitle.Length > 1024
            || string.IsNullOrWhiteSpace(source.ResolvedTitle)
            || source.ResolvedTitle.Length > 1024
            || source.Anchor?.Length > 2048)
        || turn.ToolTrace is null
        || turn.ToolTrace.Count > AiTextLimits.MaximumToolAttempts
        || turn.ToolTrace.Any(call => call is null
            || call.Name is not ("search_wiki" or "read_wiki")
            || call.Status is not ("succeeded" or "not_found" or "failed"))
        || !Enum.IsDefined(turn.Status)
        || turn.Usage is { } usage && (usage.Requests < 0
            || usage.ToolCalls < 0
            || usage.InputTokens < 0
            || usage.CachedInputTokens < 0
            || usage.CachedInputTokens > usage.InputTokens
            || usage.OutputTokens < 0
            || usage.TotalTokens != usage.InputTokens + usage.OutputTokens
            || usage.CostUsd < 0)
        || turn.CreatedAt == default
        || turn.UpdatedAt < turn.CreatedAt;

    private static bool IsValidIndex(ChatConversationIndex index) =>
        index.Version == CurrentVersion
        && index.Items is not null
        && index.Items.All(item => item is not null)
        && index.Items.Count(item => item.IsFavorite) <= MaxFavorites
        && index.Items.Count(item => !item.IsFavorite) <= MaxUnfavorited
        && index.Items.Select(item => item.Id).Distinct().Count() == index.Items.Count
        && index.Items.All(item => item.Id != Guid.Empty
            && !string.IsNullOrWhiteSpace(item.Title)
            && item.Title.Length <= 61
            && item.CreatedAt != default
            && item.UpdatedAt >= item.CreatedAt
            && item.TurnCount is >= 1 and <= MaxTurns);
}

internal sealed record ChatConversationIndex(
    int Version,
    IReadOnlyList<ChatConversationIndexItem> Items)
{
    public ChatConversationIndex() : this(1, []) { }
}

internal sealed record ChatConversationIndexItem(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsFavorite,
    int TurnCount);

internal sealed class ChatConversationStoreException : IOException
{
    public ChatConversationStoreException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
