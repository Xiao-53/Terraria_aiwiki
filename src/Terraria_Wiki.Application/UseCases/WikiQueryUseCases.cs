using Terraria_Wiki.Application.Internal;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Application;

public sealed class WikiQueryUseCases
{
    private readonly ApplicationState _state;

    internal WikiQueryUseCases(ApplicationState state) => _state = state;

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string keyword,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        _state.RequireActiveBook();
        var limit = maxResults ?? _state.RequirePreferences().SearchLimit;
        return await _state.Reader.SearchAsync(
            keyword,
            new WikiSearchOptions(limit),
            cancellationToken);
    }

    public async Task<WikiReadResponse> ReadAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        _state.RequireActiveBook();
        var result = await _state.Reader.ReadAsync(title, cancellationToken);
        return ApplicationMapper.ToResponse(result);
    }

    public async Task<WikiAssetContent?> ReadAssetAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("An asset file name is required.", nameof(fileName));
        _state.RequireActiveBook();
        var asset = await _state.ContentDatabase.GetItemAsync<WikiAsset>(fileName, cancellationToken);
        return asset is null
            ? null
            : new WikiAssetContent(
                asset.FileName,
                asset.Data,
                asset.MimeType,
                asset.LastModified);
    }
}
