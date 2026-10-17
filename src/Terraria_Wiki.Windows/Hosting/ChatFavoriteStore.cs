using System.Text.Json;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Presentation.Favorites;

namespace Terraria_Wiki.Windows.Hosting;

internal sealed class ChatFavoriteStore : IChatFavoriteStore
{
    private const int CurrentVersion = 1;
    private const int MaxFavorites = 200;
    private const int MaxQuestionLength = 4 * 1024;
    private const int MaxMarkdownLength = 32 * 1024;
    private const int MaxSources = 32;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public ChatFavoriteStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathFullyQualified(filePath))
            throw new ArgumentException("An absolute chat favorites path is required.", nameof(filePath));
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }
    internal string BackupPath => FilePath + ".backup";

    public static ChatFavoriteStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new ChatFavoriteStoreException("chat-favorites-path-unavailable", "对话收藏目录不可用。");
        return new ChatFavoriteStore(Path.Combine(localAppData, "Terraria AI Wiki", "chat-favorites.json"));
    }

    public async Task<IReadOnlyList<ChatFavoriteRecord>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadDocumentAsync(cancellationToken).ConfigureAwait(false)).Items
                .OrderByDescending(item => item.FavoritedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        ChatFavoriteRecord favorite,
        CancellationToken cancellationToken = default)
    {
        Validate(favorite);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            var items = current.Items.Where(item => item.Id != favorite.Id).ToList();
            items.Insert(0, favorite);
            await WriteDocumentAsync(
                new ChatFavoriteDocument(CurrentVersion, items.Take(MaxFavorites).ToArray()),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            var items = current.Items.Where(item => item.Id != id).ToArray();
            if (items.Length == current.Items.Count)
                return;
            await WriteDocumentAsync(new ChatFavoriteDocument(CurrentVersion, items), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ChatFavoriteDocument> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath) && !File.Exists(BackupPath))
            return new ChatFavoriteDocument();

        var primary = await TryReadAsync(FilePath, cancellationToken).ConfigureAwait(false);
        if (primary is not null)
            return primary;
        var backup = await TryReadAsync(BackupPath, cancellationToken).ConfigureAwait(false);
        if (backup is not null)
            return backup;
        throw new ChatFavoriteStoreException("chat-favorites-invalid", "对话收藏文件无法读取。");
    }

    private static async Task<ChatFavoriteDocument?> TryReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<ChatFavoriteDocument>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (document is not { Version: CurrentVersion } || document.Items.Count > MaxFavorites)
                return null;
            foreach (var item in document.Items)
                Validate(item);
            return document;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private async Task WriteDocumentAsync(
        ChatFavoriteDocument document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(FilePath))
                File.Replace(tempPath, FilePath, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ChatFavoriteStoreException("chat-favorites-write-failed", "无法保存对话收藏。", exception);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static void Validate(ChatFavoriteRecord favorite)
    {
        if (favorite.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(favorite.Question)
            || favorite.Question.Length > MaxQuestionLength
            || string.IsNullOrWhiteSpace(favorite.Markdown)
            || AiTextLimits.Count(favorite.Markdown) > MaxMarkdownLength
            || favorite.Sources.Count > MaxSources
            || favorite.Sources.Any(source => string.IsNullOrWhiteSpace(source.ResolvedTitle)))
        {
            throw new ArgumentException("The chat favorite is invalid.", nameof(favorite));
        }
    }
}

internal sealed record ChatFavoriteDocument
{
    public ChatFavoriteDocument()
    {
    }

    public ChatFavoriteDocument(int version, IReadOnlyList<ChatFavoriteRecord> items)
    {
        Version = version;
        Items = items;
    }

    public int Version { get; init; } = 1;
    public IReadOnlyList<ChatFavoriteRecord> Items { get; init; } = [];
}

internal sealed class ChatFavoriteStoreException : IOException
{
    public ChatFavoriteStoreException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
