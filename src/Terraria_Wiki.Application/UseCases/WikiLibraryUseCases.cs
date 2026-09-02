using System.Globalization;
using Terraria_Wiki.Application.Internal;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Application;

public sealed class WikiLibraryUseCases
{
    private readonly ApplicationState _state;

    internal WikiLibraryUseCases(ApplicationState state) => _state = state;

    public async Task<PageSlice<WikiPageListItem>> GetPagesAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _state.RequireActiveBook();
        var filter = NormalizeFilter(request.FilterText);
        int total;
        IReadOnlyList<WikiPageSummary> rows;
        if (filter is null)
        {
            total = await _state.ContentDatabase.GetCountAsync<WikiPage>(cancellationToken);
            rows = await _state.ContentDatabase.GetPageSummariesPagedAsync(
                request.Offset,
                request.Limit,
                cancellationToken);
        }
        else
        {
            total = await _state.ContentDatabase.GetFilteredCountAsync<WikiPage>(
                "Title",
                filter,
                cancellationToken);
            rows = await _state.ContentDatabase.GetFilteredPagedAsync<WikiPageSummary>(
                "WikiPage",
                "Title, LastModified",
                "Title",
                filter,
                "Title",
                request.Offset,
                request.Limit,
                isDesc: false,
                cancellationToken);
        }

        return new PageSlice<WikiPageListItem>(
            rows.Select(row => new WikiPageListItem(row.Title, row.LastModified)).ToArray(),
            total,
            request.Offset,
            request.Limit);
    }

    public async Task<PageSlice<WikiHistoryItem>> GetHistoryAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _state.RequireActiveBook();
        var filter = NormalizeFilter(request.FilterText);
        int total;
        IReadOnlyList<WikiHistory> rows;
        if (filter is null)
        {
            total = await _state.ContentDatabase.GetCountAsync<WikiHistory>(cancellationToken);
            rows = await _state.ContentDatabase.GetWikiHistoryPagedAsync(
                request.Offset,
                request.Limit,
                cancellationToken);
        }
        else
        {
            total = await _state.ContentDatabase.GetFilteredCountAsync<WikiHistory>(
                "WikiTitle",
                filter,
                cancellationToken);
            rows = await _state.ContentDatabase.GetFilteredPagedAsync<WikiHistory>(
                "WikiTitle",
                filter,
                "ReadAt",
                request.Offset,
                request.Limit,
                isDesc: true,
                cancellationToken);
        }

        return new PageSlice<WikiHistoryItem>(
            rows.Select(row => new WikiHistoryItem(row.Id, row.WikiTitle, row.ReadAt, row.DateKey)).ToArray(),
            total,
            request.Offset,
            request.Limit);
    }

    public async Task<PageSlice<WikiFavoriteItem>> GetFavoritesAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _state.RequireActiveBook();
        var filter = NormalizeFilter(request.FilterText);
        int total;
        IReadOnlyList<WikiFavorite> rows;
        if (filter is null)
        {
            total = await _state.ContentDatabase.GetCountAsync<WikiFavorite>(cancellationToken);
            rows = await _state.ContentDatabase.GetWikiFavoritePagedAsync(
                request.Offset,
                request.Limit,
                cancellationToken);
        }
        else
        {
            total = await _state.ContentDatabase.GetFilteredCountAsync<WikiFavorite>(
                "WikiTitle",
                filter,
                cancellationToken);
            rows = await _state.ContentDatabase.GetFilteredPagedAsync<WikiFavorite>(
                "WikiTitle",
                filter,
                "FavoritedAt",
                request.Offset,
                request.Limit,
                isDesc: true,
                cancellationToken);
        }

        return new PageSlice<WikiFavoriteItem>(
            rows.Select(row => new WikiFavoriteItem(row.WikiTitle, row.FavoritedAt)).ToArray(),
            total,
            request.Offset,
            request.Limit);
    }

    public async Task<bool> RecordVisitAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A page title is required.", nameof(title));
        return await RunWriteAsync(async active =>
        {
            var pageTitle = GetPageTitle(title);
            if (string.Equals(pageTitle, active.DefaultPageTitle, StringComparison.Ordinal))
                return false;

            var now = _state.TimeProvider.GetLocalNow().DateTime;
            await _state.ContentDatabase.SaveHistoryAsync(new WikiHistory
            {
                WikiTitle = pageTitle,
                ReadAt = now,
                DateKey = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }, cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await RunWriteAsync(async _ =>
        {
            await _state.ContentDatabase.DeleteItemsAsync<WikiHistory>(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> IsFavoriteAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A page title is required.", nameof(title));
        _state.RequireActiveBook();
        return await _state.ContentDatabase.ItemExistsAsync<WikiFavorite>(title, cancellationToken);
    }

    public async Task<bool> AddFavoriteAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A page title is required.", nameof(title));
        return await RunWriteAsync(async _ =>
        {
            if (await _state.ContentDatabase.ItemExistsAsync<WikiFavorite>(title, cancellationToken))
                return false;

            await _state.ContentDatabase.SaveItemAsync(new WikiFavorite
            {
                WikiTitle = title,
                FavoritedAt = _state.TimeProvider.GetLocalNow().DateTime,
            }, cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> RemoveFavoriteAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A page title is required.", nameof(title));
        return await RunWriteAsync(async _ =>
        {
            if (!await _state.ContentDatabase.ItemExistsAsync<WikiFavorite>(title, cancellationToken))
                return false;

            await _state.ContentDatabase.DeleteItemAsync<WikiFavorite>(title, cancellationToken);
            return true;
        }, cancellationToken);
    }

    private async Task<WikiDataOperationGate.WikiDataOperationLease> EnterAsync(
        CancellationToken cancellationToken)
    {
        var operation = await _state.OperationGate.TryAcquireAsync(cancellationToken);
        return operation ?? throw new ApplicationUseCaseException(
            "operation-in-progress",
            "Another Wiki data operation is already running.");
    }

    private async Task<T> RunWriteAsync<T>(
        Func<WikiBook, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
        {
            throw new ApplicationUseCaseException(
                "operation-in-progress",
                "Another Wiki data operation is already running.");
        }

        try
        {
            var active = _state.RequireActiveBook();
            using var operation = await EnterAsync(cancellationToken);
            return await action(active);
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    private static string GetPageTitle(string title)
    {
        var hash = title.IndexOf('#');
        return hash < 0 ? title : title[..hash];
    }

    private static string? NormalizeFilter(string? filter) =>
        string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
}
