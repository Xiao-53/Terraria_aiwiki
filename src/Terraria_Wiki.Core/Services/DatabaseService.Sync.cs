using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Models;
using System.Text.Json;

namespace Terraria_Wiki.Services;

public partial class DatabaseService
{
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
            if (target is not null)
                transaction.Insert(new WikiRedirect { FromName = title, ToTarget = target });
        });
        cancellationToken.ThrowIfCancellationRequested();
    }
}
