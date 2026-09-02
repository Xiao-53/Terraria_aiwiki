using SQLite;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Query;

public sealed record WikiSearchOptions
{
    public WikiSearchOptions(int maxResults = 50)
    {
        if (maxResults is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxResults), "The maximum result count must be between 1 and 100.");

        MaxResults = maxResults;
    }

    public int MaxResults { get; }
}

public enum WikiReadStatus
{
    Found,
    NotFound,
}

public sealed record WikiRedirectResolution(
    string FromTitle,
    string TargetTitle,
    string? TargetAnchor);

public sealed record WikiReadResult(
    string RequestedTitle,
    string RequestedPageTitle,
    WikiReadStatus Status,
    WikiPage? Page,
    WikiRedirectResolution? Redirect,
    string? Anchor)
{
    public bool IsFound => Status == WikiReadStatus.Found;
    public bool IsRedirect => Redirect is not null;
    public string? ResolvedTitle => Page?.Title;
}

public enum WikiQueryErrorCode
{
    DataSourceUnavailable,
}

public sealed class WikiQueryException : Exception
{
    public WikiQueryException(WikiQueryErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public WikiQueryErrorCode Code { get; }
}

/// <summary>
/// Provides the portable Search/Read boundary over a content database.
/// </summary>
public sealed class WikiReader
{
    private readonly DatabaseService _database;

    public WikiReader(DatabaseService database)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (database.Mode != DbMode.Content)
            throw new ArgumentException("WikiReader requires a content database.", nameof(database));

        _database = database;
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string keyword,
        WikiSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _database.SearchAsync(keyword, options.MaxResults, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDataSourceFailure(ex))
        {
            throw CreateUnavailableException(ex);
        }
    }

    public async Task<WikiReadResult> ReadAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A page title is required.", nameof(title));

        cancellationToken.ThrowIfCancellationRequested();
        var (requestedPageTitle, explicitAnchor, hasExplicitAnchor) = SplitTitleAndAnchor(title);

        try
        {
            var page = await _database.GetItemAsync<WikiPage>(requestedPageTitle, cancellationToken);
            if (page is not null)
            {
                return new WikiReadResult(
                    title,
                    requestedPageTitle,
                    WikiReadStatus.Found,
                    page,
                    null,
                    explicitAnchor);
            }

            // Preserve the legacy navigation rule: an explicit anchor means the
            // caller already selected a page identity, so redirect lookup is skipped.
            if (hasExplicitAnchor)
            {
                return new WikiReadResult(
                    title,
                    requestedPageTitle,
                    WikiReadStatus.NotFound,
                    null,
                    null,
                    explicitAnchor);
            }

            var redirect = await _database.GetItemAsync<WikiRedirect>(requestedPageTitle, cancellationToken);
            if (redirect is null)
            {
                return new WikiReadResult(
                    title,
                    requestedPageTitle,
                    WikiReadStatus.NotFound,
                    null,
                    null,
                    explicitAnchor);
            }

            var (targetTitle, targetAnchor, _) = SplitTitleAndAnchor(redirect.ToTarget);
            var resolution = new WikiRedirectResolution(redirect.FromName, targetTitle, targetAnchor);
            var resolvedPage = await _database.GetItemAsync<WikiPage>(targetTitle, cancellationToken);

            return new WikiReadResult(
                title,
                requestedPageTitle,
                resolvedPage is null ? WikiReadStatus.NotFound : WikiReadStatus.Found,
                resolvedPage,
                resolution,
                targetAnchor);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDataSourceFailure(ex))
        {
            throw CreateUnavailableException(ex);
        }
    }

    private WikiQueryException CreateUnavailableException(Exception innerException) =>
        new(
            WikiQueryErrorCode.DataSourceUnavailable,
            $"The Wiki data source at '{_database.DatabasePath}' is unavailable.",
            innerException);

    private static bool IsDataSourceFailure(Exception exception) =>
        exception is SQLiteException
            or IOException
            or UnauthorizedAccessException
            or ObjectDisposedException
            or InvalidOperationException;

    private static (string Title, string? Anchor, bool HasAnchor) SplitTitleAndAnchor(string value)
    {
        var hashIndex = value.IndexOf('#');
        return hashIndex < 0
            ? (value, null, false)
            : (value[..hashIndex], value[(hashIndex + 1)..], true);
    }
}
