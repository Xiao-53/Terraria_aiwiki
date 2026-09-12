using Terraria_Wiki.Application.Internal;
using Terraria_Wiki.Core.Data;
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

    public async Task<GlobalWikiFavoritesResult> GetAllFavoritesAsync(
        CancellationToken cancellationToken = default)
    {
        return await RunCoordinatedAsync(async () =>
        {
            var active = _state.RequireActiveBook();
            var books = await _state.ManagerDatabase.GetItemsAsync<WikiBook>(cancellationToken);
            var items = new List<GlobalWikiFavoriteItem>();
            var issues = new List<GlobalWikiFavoriteIssue>();
            foreach (var book in books.OrderBy(book => book.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    IReadOnlyList<WikiFavorite> favorites;
                    if (book.Id == active.Id)
                    {
                        favorites = await ReadActiveFavoritesAsync(cancellationToken);
                    }
                    else
                    {
                        var read = WikiFavoriteStore.ReadAllWithStatus(
                            WikiDataPaths.Create(_state.DataRoot, book),
                            cancellationToken);
                        favorites = read.Items;
                        if (read.IssueCode is not null)
                            issues.Add(new GlobalWikiFavoriteIssue(book.Id, book.Title, read.IssueCode));
                    }
                    items.AddRange(favorites.Select(item => new GlobalWikiFavoriteItem(
                        book.Id,
                        book.Title,
                        item.WikiTitle,
                        item.FavoritedAt)));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (WikiFavoriteStoreException exception)
                {
                    issues.Add(new GlobalWikiFavoriteIssue(book.Id, book.Title, exception.Code));
                }
                catch
                {
                    issues.Add(new GlobalWikiFavoriteIssue(book.Id, book.Title, "favorite-database-unreadable"));
                }
            }

            var ordered = items
                .OrderByDescending(item => item.FavoritedAt)
                .ThenBy(item => item.WikiId)
                .ThenBy(item => item.PageTitle, StringComparer.Ordinal)
                .ToArray();
            return new GlobalWikiFavoritesResult(ordered, issues);
        }, cancellationToken);
    }

    public async Task<bool> IsFavoriteAsync(
        int wikiId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ValidateWikiIdentity(wikiId, title);
        return await RunCoordinatedAsync(async () =>
        {
            var active = _state.RequireActiveBook();
            if (active.Id == wikiId)
                return await _state.ContentDatabase.ItemExistsAsync<WikiFavorite>(title, cancellationToken);
            var book = await FindBookAsync(wikiId, cancellationToken);
            return WikiFavoriteStore.IsFavorite(WikiDataPaths.Create(_state.DataRoot, book), title, cancellationToken);
        }, cancellationToken);
    }

    public async Task<bool> SetFavoriteAsync(
        int wikiId,
        string title,
        bool favorite,
        CancellationToken cancellationToken = default)
    {
        ValidateWikiIdentity(wikiId, title);
        return await RunCoordinatedAsync(async () =>
        {
            var active = _state.RequireActiveBook();
            if (active.Id == wikiId)
            {
                var exists = await _state.ContentDatabase.ItemExistsAsync<WikiFavorite>(title, cancellationToken);
                if (favorite && !exists)
                {
                    await _state.ContentDatabase.SaveItemAsync(new WikiFavorite
                    {
                        WikiTitle = title,
                        FavoritedAt = _state.TimeProvider.GetLocalNow().DateTime,
                    }, cancellationToken);
                }
                else if (!favorite && exists)
                {
                    await _state.ContentDatabase.DeleteItemAsync<WikiFavorite>(title, cancellationToken);
                }
                return favorite;
            }

            var book = await FindBookAsync(wikiId, cancellationToken);
            try
            {
                return WikiFavoriteStore.SetFavorite(
                    WikiDataPaths.Create(_state.DataRoot, book),
                    title,
                    favorite,
                    _state.TimeProvider.GetLocalNow().DateTime,
                    cancellationToken);
            }
            catch (WikiFavoriteStoreException exception)
            {
                throw new ApplicationUseCaseException(exception.Code, "The Wiki favorite could not be updated.", exception);
            }
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<WikiFavorite>> ReadActiveFavoritesAsync(CancellationToken cancellationToken)
    {
        var total = await _state.ContentDatabase.GetCountAsync<WikiFavorite>(cancellationToken);
        return total == 0
            ? []
            : await _state.ContentDatabase.GetWikiFavoritePagedAsync(0, total, cancellationToken);
    }

    private async Task<WikiBook> FindBookAsync(int wikiId, CancellationToken cancellationToken) =>
        await _state.ManagerDatabase.GetItemAsync<WikiBook>(wikiId, cancellationToken)
        ?? throw new ApplicationUseCaseException("unknown-wiki", $"WikiBook {wikiId} is not registered.");

    private async Task<T> RunCoordinatedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
            throw new ApplicationUseCaseException("operation-in-progress", "Another Wiki data operation is already running.");
        try
        {
            using var operation = await EnterAsync(cancellationToken);
            return await action();
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    private static void ValidateWikiIdentity(int wikiId, string title)
    {
        if (wikiId <= 0)
            throw new ArgumentOutOfRangeException(nameof(wikiId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A page title is required.", nameof(title));
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

    private static string? NormalizeFilter(string? filter) =>
        string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
}
