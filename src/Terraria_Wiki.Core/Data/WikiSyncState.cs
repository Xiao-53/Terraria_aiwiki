using System.Text.Json;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Data;

internal sealed record WikiSyncState
{
    public int Version { get; init; } = 1;
    public string Identity { get; init; } = string.Empty;
    public int FormatVersion { get; init; } = 2;
    public int ExtractorVersion { get; init; } = 2;
    public DateTimeOffset CoveredThrough { get; init; }
    public long AnchorId { get; init; }
    public DateTimeOffset AnchorTime { get; init; }
    public DateTimeOffset LastFullScan { get; init; }
    public bool Incomplete { get; init; }
    public bool ResourcesCovered { get; init; }

    public static string IdentityFor(WikiBook book) => new Uri(book.ApiBaseUrl).AbsoluteUri.TrimEnd('/') + "|" +
        string.Join(',', book.AdditionalNamespaces.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse).Append(book.MainNamespace).Distinct().Order());

    public bool Matches(WikiBook book) => Version == 1 && FormatVersion == 2 && ExtractorVersion == 2 &&
        string.Equals(Identity, IdentityFor(book), StringComparison.Ordinal);

    public static WikiSyncState? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var name in new[] { "Version", "Identity", "FormatVersion", "ExtractorVersion", "CoveredThrough",
                         "AnchorId", "AnchorTime", "LastFullScan", "Incomplete" })
                if (!document.RootElement.TryGetProperty(name, out _)) return null;
            return JsonSerializer.Deserialize<WikiSyncState>(json);
        }
        catch (JsonException) { return null; }
    }

    // Called only after the enclosing database's hash, schema and wiki_id have
    // been verified. This establishes compatibility, not remote log retention;
    // the next explicit update still validates the anchor with the server.
    public static bool CanPreserveOnImport(string? json, WikiBook book)
    {
        try
        {
            var state = Parse(json);
            return state is not null && state.Matches(book) && state.CoveredThrough != default &&
                state.LastFullScan != default && state.LastFullScan <= state.CoveredThrough &&
                state.AnchorId > 0 && state.AnchorTime != default && state.AnchorTime <= state.CoveredThrough;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        { return false; }
    }
}

internal sealed record SyncPage(string Title, int Namespace, long PageId, DateTime Modified, bool Redirect, bool Missing);
internal sealed record SyncEvent(long Id, DateTimeOffset Time, string Title, int Namespace, string Type, string? LogType, string? Target);
internal sealed class SyncCoverageException(string message) : Exception(message);

// These projections deliberately exclude HTML and media blobs.
public sealed class WikiPageMetadata
{
    public string Title { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}
