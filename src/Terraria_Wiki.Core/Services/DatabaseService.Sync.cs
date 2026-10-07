using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Models;
using System.Text.Json;

namespace Terraria_Wiki.Services;

public partial class DatabaseService
{
    internal async Task<IReadOnlyList<WikiDatabaseMetadata>> ReadSyncMetadataByPrefixAsync(string prefix, CancellationToken token)
    {
        await Init(cancellationToken: token);
        return await _db.QueryAsync<WikiDatabaseMetadata>("SELECT Key, Value FROM WikiDatabaseMetadata WHERE Key GLOB ?", prefix + "*").WaitAsync(token);
    }
    internal async Task<(Dictionary<string, bool> Tasks, bool Valid)> ReadSyncMediaQueueAsync(string identity, CancellationToken token)
    {
        await Init(cancellationToken: token);
        var header = await ReadSyncMetadataAsync("sync_media_identity", token);
        var rows = await _db.QueryAsync<WikiDatabaseMetadata>("SELECT Key, Value FROM WikiDatabaseMetadata WHERE Key GLOB 'sync_media_pending:*'").WaitAsync(token);
        var tasks = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (rows.Count == 0) return (tasks, header is null || header == "1|" + identity);
        if (header != "1|" + identity) return (tasks, false);
        foreach (var row in rows)
        {
            try
            {
                using var document = JsonDocument.Parse(row.Value);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("Url", out var url) || url.ValueKind != JsonValueKind.String ||
                    !document.RootElement.TryGetProperty("Force", out var force) || force.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return ([], false);
                var task = JsonSerializer.Deserialize<WikiSyncMediaTask>(row.Value);
                if (task is null || !Uri.TryCreate(task.Url, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https") || row.Key != WikiSyncMediaTask.Key(task.Url)) return ([], false);
                tasks[task.Url] = task.Force;
            }
            catch (JsonException) { return ([], false); }
        }
        return (tasks, true);
    }

    internal async Task SaveSyncMediaQueueAsync(string identity, IReadOnlyDictionary<string, bool> tasks, CancellationToken token)
    {
        await Init(cancellationToken: token);
        token.ThrowIfCancellationRequested();
        await _db.RunInTransactionAsync(connection =>
        {
            connection.Execute("DELETE FROM WikiDatabaseMetadata WHERE Key GLOB 'sync_media_pending:*'");
            connection.InsertOrReplace(new WikiDatabaseMetadata { Key = "sync_media_identity", Value = "1|" + identity });
            foreach (var task in tasks)
                connection.InsertOrReplace(new WikiDatabaseMetadata
                {
                    Key = WikiSyncMediaTask.Key(task.Key), Value = JsonSerializer.Serialize(new WikiSyncMediaTask(task.Key, task.Value))
                });
        });
        token.ThrowIfCancellationRequested();
    }

    internal async Task CompleteSyncMediaTaskAsync(string url, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await _db.ExecuteAsync("DELETE FROM WikiDatabaseMetadata WHERE Key = ?", WikiSyncMediaTask.Key(url));
        token.ThrowIfCancellationRequested();
    }

    internal async Task<Dictionary<string, string[]>> ReadSyncResourceUrlsAsync(IEnumerable<string>? titles, CancellationToken token)
    {
        await Init(cancellationToken: token);
        var rows = new List<WikiDatabaseMetadata>();
        if (titles is null)
            rows.AddRange(await _db.QueryAsync<WikiDatabaseMetadata>("SELECT Key, Value FROM WikiDatabaseMetadata WHERE Key GLOB 'sync_resources:*'").WaitAsync(token));
        else
            foreach (var batch in titles.Distinct(StringComparer.Ordinal).Chunk(200))
                rows.AddRange(await _db.QueryAsync<WikiDatabaseMetadata>(
                    $"SELECT Key, Value FROM WikiDatabaseMetadata WHERE Key IN ({string.Join(',', batch.Select(_ => "?"))})",
                    batch.Select(title => (object)("sync_resources:" + title)).ToArray()).WaitAsync(token));
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            try
            {
                var urls = JsonSerializer.Deserialize<string[]>(row.Value);
                if (urls is not null && urls.All(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
                    result[row.Key["sync_resources:".Length..]] = urls;
            }
            catch (JsonException) { }
        }
        return result;
    }

    public async Task<IReadOnlyList<WikiPageMetadata>> GetPageMetadataAsync(
        IEnumerable<string>? titles = null, CancellationToken cancellationToken = default)
    {
        await Init(cancellationToken: cancellationToken);
        if (titles is null)
            return await _db.QueryAsync<WikiPageMetadata>("SELECT Title, LastModified FROM WikiPage").WaitAsync(cancellationToken);
        var result = new List<WikiPageMetadata>();
        foreach (var batch in titles.Distinct(StringComparer.Ordinal).Chunk(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.AddRange(await _db.QueryAsync<WikiPageMetadata>(
                $"SELECT Title, LastModified FROM WikiPage WHERE Title IN ({string.Join(',', batch.Select(_ => "?"))})",
                batch.Cast<object>().ToArray()).WaitAsync(cancellationToken));
        }
        return result;
    }

    internal async Task<string?> ReadSyncMetadataAsync(string key, CancellationToken cancellationToken)
    {
        await Init(cancellationToken: cancellationToken);
        return await _db.ExecuteScalarAsync<string>("SELECT Value FROM WikiDatabaseMetadata WHERE Key = ?", key)
            .WaitAsync(cancellationToken);
    }

    internal async Task WriteSyncMetadataAsync(string key, string value, CancellationToken cancellationToken)
    {
        await Init(cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // sqlite-net work cannot be stopped by cancelling its await. Keep the operation gate
        // until the actual write ends, then observe cancellation.
        await _db.InsertOrReplaceAsync(new WikiDatabaseMetadata { Key = key, Value = value });
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal async Task ApplySyncRedirectAsync(string title, string? target, CancellationToken cancellationToken)
    {
        await Init(cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _db.RunInTransactionAsync(transaction =>
        {
            DeleteRegionIndex(transaction, "WHERE PageTitle = ?", title);
            transaction.Execute("DELETE FROM WikiSearchSection WHERE PageTitle = ?", title);
            transaction.Execute("DELETE FROM WikiPage WHERE Title = ?", title);
            transaction.Execute("DELETE FROM WikiRedirect WHERE FromName = ?", title);
            transaction.Execute("DELETE FROM WikiDatabaseMetadata WHERE Key = ?", "sync_resources:" + title);
            if (target is not null)
                transaction.Insert(new WikiRedirect { FromName = title, ToTarget = target });
        });
        cancellationToken.ThrowIfCancellationRequested();
    }
}
