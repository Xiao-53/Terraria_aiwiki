using SQLite;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Services;

public enum DbMode
{
    Manager,
    Content,
}

/// <summary>
/// Portable sqlite-net persistence service. Its schema, seeds, and FTS table
/// retain the legacy application's data contract; Search behavior is fixed by
/// the stable Wiki query boundary.
/// </summary>
public class DatabaseService
{
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private SQLiteAsyncConnection _db;

    // Kept public for source compatibility with the legacy service.
    public bool _initialized;

    public DatabaseService(string dbPath, DbMode mode)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("A database path is required.", nameof(dbPath));

        DatabasePath = dbPath;
        Mode = mode;
        EnsureDatabaseDirectory(DatabasePath);
        _db = CreateConnection(DatabasePath);
    }

    public string DatabasePath { get; private set; }
    public DbMode Mode { get; }

    public async Task Init(
        bool force = false,
        WikiBook? seedBook = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_initialized && !force)
            return;

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && !force)
                return;

            await _db.ExecuteScalarAsync<string>("PRAGMA journal_mode = WAL;")
                .WaitAsync(cancellationToken);

            if (Mode == DbMode.Manager)
            {
                await _db.CreateTableAsync<WikiBook>().WaitAsync(cancellationToken);
                await MigrateWikiBookColumnsAsync(cancellationToken);
                await SeedWikiBooksAsync(cancellationToken);
            }
            else
            {
                await _db.CreateTableAsync<WikiPage>().WaitAsync(cancellationToken);
                await _db.CreateTableAsync<WikiRedirect>().WaitAsync(cancellationToken);
                await _db.CreateTableAsync<WikiHistory>().WaitAsync(cancellationToken);
                await _db.CreateTableAsync<WikiFavorite>().WaitAsync(cancellationToken);
                await _db.CreateTableAsync<WikiAsset>().WaitAsync(cancellationToken);
                await InitFtsTableCoreAsync(cancellationToken);
                await SeedWikiPageAsync(seedBook, cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task InitFtsTableAsync(CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            await InitFtsTableCoreAsync(cancellationToken);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public SQLiteAsyncConnection GetConnection() => _db;

    public async Task CloseConnection(CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                await _db.ExecuteScalarAsync<string>("PRAGMA journal_mode = DELETE;")
                    .WaitAsync(cancellationToken);
            }

            await _db.CloseAsync().WaitAsync(cancellationToken);
            _initialized = false;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task SwitchDatabaseAsync(
        string newDbPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newDbPath))
            throw new ArgumentException("A database path is required.", nameof(newDbPath));

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            await _db.CloseAsync().WaitAsync(cancellationToken);
            _initialized = false;
            DatabasePath = newDbPath;
            EnsureDatabaseDirectory(DatabasePath);
            _db = CreateConnection(DatabasePath);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            _initialized = false;
            await _db.CloseAsync().WaitAsync(cancellationToken);
            EnsureDatabaseDirectory(DatabasePath);
            _db = CreateConnection(DatabasePath);
        }
        finally
        {
            _connectionGate.Release();
        }

        await Init(cancellationToken: cancellationToken);
    }

    public Task DeleteDatabaseFile(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(DatabasePath);
        return Task.CompletedTask;
    }

    public async Task BackupAsync(
        string destinationDatabasePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationDatabasePath))
            throw new ArgumentException("A destination database path is required.", nameof(destinationDatabasePath));

        await Init(cancellationToken: cancellationToken);
        EnsureDatabaseDirectory(destinationDatabasePath);
        await _db.BackupAsync(destinationDatabasePath, "main").WaitAsync(cancellationToken);
    }

    public async Task BackupExistingAsync(
        string destinationDatabasePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(DatabasePath))
            throw new FileNotFoundException("The source database does not exist.", DatabasePath);
        if (string.IsNullOrWhiteSpace(destinationDatabasePath))
            throw new ArgumentException("A destination database path is required.", nameof(destinationDatabasePath));

        cancellationToken.ThrowIfCancellationRequested();
        EnsureDatabaseDirectory(destinationDatabasePath);
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            await _db.BackupAsync(destinationDatabasePath, "main").WaitAsync(cancellationToken);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task SaveItemAsync<T>(T item, CancellationToken cancellationToken = default)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(item);
        await Init(cancellationToken: cancellationToken);
        await _db.InsertOrReplaceAsync(item).WaitAsync(cancellationToken);
    }

    public async Task SaveItemsAsync<T>(
        IEnumerable<T> items,
        CancellationToken cancellationToken = default)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(items);
        await Init(cancellationToken: cancellationToken);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _db.InsertOrReplaceAsync(item).WaitAsync(cancellationToken);
        }
    }

    public async Task<int> GetCountAsync<T>(CancellationToken cancellationToken = default)
        where T : new()
    {
        await Init(cancellationToken: cancellationToken);
        return await _db.Table<T>().CountAsync().WaitAsync(cancellationToken);
    }

    public async Task DeleteItemAsync<T>(
        object primaryKey,
        CancellationToken cancellationToken = default)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(primaryKey);
        await Init(cancellationToken: cancellationToken);
        var item = await _db.FindAsync<T>(primaryKey).WaitAsync(cancellationToken);
        if (item is not null)
            await _db.DeleteAsync(item).WaitAsync(cancellationToken);
    }

    public async Task DeleteItemsAsync<T>(CancellationToken cancellationToken = default)
        where T : new()
    {
        await Init(cancellationToken: cancellationToken);
        await _db.DeleteAllAsync<T>().WaitAsync(cancellationToken);
    }

    public async Task<List<T>> GetItemsAsync<T>(CancellationToken cancellationToken = default)
        where T : new()
    {
        await Init(cancellationToken: cancellationToken);
        return await _db.Table<T>().ToListAsync().WaitAsync(cancellationToken);
    }

    public async Task<List<string>> GetAllPrimaryKeysAsync<T>(CancellationToken cancellationToken = default)
        where T : new()
    {
        await Init(cancellationToken: cancellationToken);
        var mapping = _db.GetConnection().GetMapping<T>();
        var pk = mapping.PK
            ?? throw new InvalidOperationException($"{typeof(T).Name} does not define a primary key.");
        return await _db.QueryScalarsAsync<string>(
                $"SELECT \"{pk.Name}\" FROM \"{mapping.TableName}\"")
            .WaitAsync(cancellationToken);
    }

    public async Task<T?> GetItemAsync<T>(
        object primaryKey,
        CancellationToken cancellationToken = default)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(primaryKey);
        await Init(cancellationToken: cancellationToken);
        return await _db.FindAsync<T>(primaryKey).WaitAsync(cancellationToken);
    }

    public async Task<bool> ItemExistsAsync<T>(
        object primaryKey,
        CancellationToken cancellationToken = default)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(primaryKey);
        await Init(cancellationToken: cancellationToken);
        var mapping = _db.GetConnection().GetMapping(typeof(T));
        var pk = mapping.PK
            ?? throw new InvalidOperationException($"{typeof(T).Name} does not define a primary key.");
        var query = $"SELECT COUNT(*) FROM \"{mapping.TableName}\" WHERE \"{pk.Name}\" = ?";
        var count = await _db.ExecuteScalarAsync<int>(query, primaryKey).WaitAsync(cancellationToken);
        return count > 0;
    }

    public async Task SaveHistoryAsync(
        WikiHistory wikiHistory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wikiHistory);
        await Init(cancellationToken: cancellationToken);
        var existingItem = await _db.Table<WikiHistory>()
            .Where(x => x.WikiTitle == wikiHistory.WikiTitle && x.DateKey == wikiHistory.DateKey)
            .FirstOrDefaultAsync()
            .WaitAsync(cancellationToken);

        if (existingItem is not null)
        {
            wikiHistory.Id = existingItem.Id;
            await _db.UpdateAsync(wikiHistory).WaitAsync(cancellationToken);
        }
        else
        {
            await _db.InsertAsync(wikiHistory).WaitAsync(cancellationToken);
        }
    }

    public async Task<List<WikiPageSummary>> GetPageSummariesPagedAsync(
        int startIndex,
        int count,
        CancellationToken cancellationToken = default)
    {
        await Init(cancellationToken: cancellationToken);
        const string sql = "SELECT Title, LastModified FROM WikiPage ORDER BY Title LIMIT ? OFFSET ?";
        return await _db.QueryAsync<WikiPageSummary>(sql, count, startIndex).WaitAsync(cancellationToken);
    }

    public async Task<List<WikiHistory>> GetWikiHistoryPagedAsync(
        int startIndex,
        int count,
        CancellationToken cancellationToken = default)
    {
        await Init(cancellationToken: cancellationToken);
        const string sql = "SELECT * FROM WikiHistory ORDER BY ReadAt DESC LIMIT ? OFFSET ?";
        return await _db.QueryAsync<WikiHistory>(sql, count, startIndex).WaitAsync(cancellationToken);
    }

    public async Task<List<WikiFavorite>> GetWikiFavoritePagedAsync(
        int startIndex,
        int count,
        CancellationToken cancellationToken = default)
    {
        await Init(cancellationToken: cancellationToken);
        const string sql = "SELECT * FROM WikiFavorite ORDER BY FavoritedAt DESC LIMIT ? OFFSET ?";
        return await _db.QueryAsync<WikiFavorite>(sql, count, startIndex).WaitAsync(cancellationToken);
    }

    public async Task VacuumDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await Init(cancellationToken: cancellationToken);
        await _db.ExecuteAsync("VACUUM").WaitAsync(cancellationToken);
    }

    public async Task SaveSearchIndexAsync(
        string title,
        string plainContent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plainContent))
            return;

        await Init(cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _db.RunInTransactionAsync(transaction =>
        {
            transaction.Execute("DELETE FROM WikiSearchIndex WHERE Title = ?", title);
            transaction.Execute(
                "INSERT INTO WikiSearchIndex (Title, PlainContent) VALUES (?, ?)",
                title,
                plainContent);
        }).WaitAsync(cancellationToken);
    }

    public async Task<List<SearchResultItem>> SearchAsync(
        string keyword,
        int maxSearchItemCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxSearchItemCount is < 1 or > 100)
            throw new ArgumentOutOfRangeException(
                nameof(maxSearchItemCount),
                "The maximum result count must be between 1 and 100.");

        var normalizedKeyword = NormalizeSearchKeyword(keyword);
        if (normalizedKeyword.Length == 0)
            return [];

        await Init(cancellationToken: cancellationToken);

        var terms = normalizedKeyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var longTerms = terms.Where(term => CountUnicodeScalars(term) >= 3).ToArray();
        var shortTerms = terms.Where(term => CountUnicodeScalars(term) < 3).ToArray();
        var escapedKeyword = EscapeLikePattern(normalizedKeyword);
        var escapedTerms = terms.Select(EscapeLikePattern).ToArray();

        var parameters = new List<object>();
        parameters.Add(escapedKeyword);
        parameters.Add(escapedKeyword);
        parameters.Add($"{escapedKeyword}%");
        parameters.Add($"{escapedKeyword}%");

        var titleContainsClause = BuildLikeConditions("p.Title", escapedTerms, parameters, includeWildcards: true);
        var redirectContainsClause = BuildLikeConditions("r.Title", escapedTerms, parameters, includeWildcards: true);

        var markerStart = $"[[[fts-start-{Guid.NewGuid():N}]]]";
        var markerEnd = $"[[[fts-end-{Guid.NewGuid():N}]]]";
        var hasBodySearch = longTerms.Length > 0;
        var bodyMatchesSql = BuildBodyMatchesSql(
            hasBodySearch,
            markerStart,
            markerEnd,
            longTerms,
            shortTerms,
            parameters);

        var sql = $"""
            WITH
            RedirectTargets AS (
                SELECT
                    redirect.FromName AS Title,
                    CASE
                        WHEN INSTR(redirect.ToTarget, '#') > 0
                            THEN SUBSTR(redirect.ToTarget, 1, INSTR(redirect.ToTarget, '#') - 1)
                        ELSE redirect.ToTarget
                    END AS RedirectTo
                FROM WikiRedirect AS redirect
                INNER JOIN WikiPage AS targetPage
                    ON targetPage.Title = CASE
                        WHEN INSTR(redirect.ToTarget, '#') > 0
                            THEN SUBSTR(redirect.ToTarget, 1, INSTR(redirect.ToTarget, '#') - 1)
                        ELSE redirect.ToTarget
                    END
            ),
            TitleExact AS (
                SELECT
                    p.Title,
                    '' AS RedirectTo,
                    0 AS MatchKind,
                    0.0 AS RankScore,
                    0 AS TieBreaker,
                    '' AS Snippet
                FROM WikiPage AS p
                WHERE p.Title LIKE ? ESCAPE '\'
            ),
            RedirectExact AS (
                SELECT
                    r.Title,
                    r.RedirectTo,
                    1 AS MatchKind,
                    0.0 AS RankScore,
                    0 AS TieBreaker,
                    '' AS Snippet
                FROM RedirectTargets AS r
                WHERE r.Title LIKE ? ESCAPE '\'
            ),
            TitlePrefix AS (
                SELECT
                    p.Title,
                    '' AS RedirectTo,
                    2 AS MatchKind,
                    0.0 AS RankScore,
                    0 AS TieBreaker,
                    '' AS Snippet
                FROM WikiPage AS p
                WHERE p.Title LIKE ? ESCAPE '\'
            ),
            RedirectPrefix AS (
                SELECT
                    r.Title,
                    r.RedirectTo,
                    3 AS MatchKind,
                    0.0 AS RankScore,
                    0 AS TieBreaker,
                    '' AS Snippet
                FROM RedirectTargets AS r
                WHERE r.Title LIKE ? ESCAPE '\'
            ),
            TitleContains AS (
                SELECT
                    p.Title,
                    '' AS RedirectTo,
                    4 AS MatchKind,
                    0.0 AS RankScore,
                    0 AS TieBreaker,
                    '' AS Snippet
                FROM WikiPage AS p
                WHERE {titleContainsClause}
            ),
            RedirectContains AS (
                SELECT
                    r.Title,
                    r.RedirectTo,
                    5 AS MatchKind,
                    0.0 AS RankScore,
                    0 AS TieBreaker,
                    '' AS Snippet
                FROM RedirectTargets AS r
                WHERE {redirectContainsClause}
            ),
            {bodyMatchesSql},
            AllMatches AS (
                SELECT Title, RedirectTo, MatchKind, RankScore, TieBreaker, Snippet FROM TitleExact
                UNION ALL SELECT Title, RedirectTo, MatchKind, RankScore, TieBreaker, Snippet FROM RedirectExact
                UNION ALL SELECT Title, RedirectTo, MatchKind, RankScore, TieBreaker, Snippet FROM TitlePrefix
                UNION ALL SELECT Title, RedirectTo, MatchKind, RankScore, TieBreaker, Snippet FROM RedirectPrefix
                UNION ALL SELECT Title, RedirectTo, MatchKind, RankScore, TieBreaker, Snippet FROM TitleContains
                UNION ALL SELECT Title, RedirectTo, MatchKind, RankScore, TieBreaker, Snippet FROM RedirectContains
                UNION ALL SELECT Title, RedirectTo, MatchKind, RankScore, TieBreaker, Snippet FROM BodyMatches
            ),
            RankedMatches AS (
                SELECT
                    allMatches.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY Title
                        ORDER BY
                            MatchKind ASC,
                            RankScore ASC,
                            LENGTH(Title) ASC,
                            Title ASC,
                            RedirectTo ASC,
                            Snippet ASC,
                            TieBreaker ASC
                    ) AS MatchNumber
                FROM AllMatches AS allMatches
            )
            SELECT Title, RedirectTo, MatchKind, RankScore, Snippet
            FROM RankedMatches
            WHERE MatchNumber = 1
            ORDER BY
                MatchKind ASC,
                RankScore ASC,
                LENGTH(Title) ASC,
                Title ASC,
                RedirectTo ASC,
                Snippet ASC,
                TieBreaker ASC
            LIMIT ?
            """;

        parameters.Add(maxSearchItemCount);
        var rows = await _db.QueryAsync<SearchQueryRow>(sql, parameters.ToArray())
            .WaitAsync(cancellationToken);

        var plainSnippets = await LoadPlainSnippetsAsync(rows, cancellationToken);
        foreach (var row in rows.Where(row => row.MatchKind != (int)WikiSearchMatchKind.BodyFts))
        {
            var contentTitle = row.RedirectTo.Length == 0 ? row.Title : row.RedirectTo;
            row.Snippet = plainSnippets.GetValueOrDefault(contentTitle, "暂无描述...");
        }

        return rows
            .Select(row => ToSearchResult(row, hasBodySearch ? markerStart : null, hasBodySearch ? markerEnd : null))
            .ToList();
    }

    private static string NormalizeSearchKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return string.Empty;

        return string.Join(
            ' ',
            keyword.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static int CountUnicodeScalars(string value) => value.EnumerateRunes().Count();

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string BuildLikeConditions(
        string column,
        IReadOnlyList<string> escapedTerms,
        ICollection<object> parameters,
        bool includeWildcards)
    {
        var clauses = new List<string>(escapedTerms.Count);
        foreach (var term in escapedTerms)
        {
            clauses.Add($"{column} LIKE ? ESCAPE '\\'");
            parameters.Add(includeWildcards ? $"%{term}%" : term);
        }

        return string.Join(" AND ", clauses);
    }

    private static string BuildBodyMatchesSql(
        bool hasBodySearch,
        string markerStart,
        string markerEnd,
        IReadOnlyList<string> longTerms,
        IReadOnlyList<string> shortTerms,
        ICollection<object> parameters)
    {
        if (!hasBodySearch)
        {
            return "BodyMatches AS (SELECT '' AS Title, '' AS RedirectTo, 6 AS MatchKind, 0.0 AS RankScore, 0 AS TieBreaker, '' AS Snippet WHERE 0)";
        }

        parameters.Add(markerStart);
        parameters.Add(markerEnd);
        parameters.Add(string.Join(
            " AND ",
            longTerms.Select(term => $"\"{term.Replace("\"", "\"\"", StringComparison.Ordinal)}\"")));

        var shortClauses = new List<string>(shortTerms.Count);
        foreach (var term in shortTerms)
        {
            shortClauses.Add("search.PlainContent LIKE ? ESCAPE '\\'");
            parameters.Add($"%{EscapeLikePattern(term)}%");
        }

        var shortClause = shortClauses.Count == 0
            ? string.Empty
            : $"\n                  AND {string.Join("\n                  AND ", shortClauses)}";

        return $"""
            BodyMatches AS (
                SELECT
                    search.Title,
                    '' AS RedirectTo,
                    6 AS MatchKind,
                    rank AS RankScore,
                    search.rowid AS TieBreaker,
                    snippet(WikiSearchIndex, 1, ?, ?, '...', 60) AS Snippet
                FROM WikiSearchIndex AS search
                INNER JOIN WikiPage AS page ON page.Title = search.Title
                WHERE WikiSearchIndex MATCH ?{shortClause}
            )
            """;
    }

    private static string BuildPlainSnippetSql(string plainContentExpression) => $"""
        CASE
            WHEN {plainContentExpression} IS NULL OR {plainContentExpression} = '' THEN '暂无描述...'
            WHEN LENGTH({plainContentExpression}) > 60 THEN SUBSTR({plainContentExpression}, 1, 60) || '...'
            ELSE {plainContentExpression}
        END
        """;

    private async Task<IReadOnlyDictionary<string, string>> LoadPlainSnippetsAsync(
        IReadOnlyList<SearchQueryRow> rows,
        CancellationToken cancellationToken)
    {
        var contentTitles = rows
            .Where(row => row.MatchKind != (int)WikiSearchMatchKind.BodyFts)
            .Select(row => row.RedirectTo.Length == 0 ? row.Title : row.RedirectTo)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (contentTitles.Length == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var placeholders = string.Join(", ", Enumerable.Repeat("?", contentTitles.Length));
        var sql = $"""
            SELECT
                Title,
                {BuildPlainSnippetSql("MAX(PlainContent)")} AS Snippet
            FROM WikiSearchIndex
            WHERE Title IN ({placeholders})
            GROUP BY Title
            """;
        var snippets = await _db.QueryAsync<PlainSnippetRow>(sql, contentTitles.Cast<object>().ToArray())
            .WaitAsync(cancellationToken);
        return snippets.ToDictionary(row => row.Title, row => row.Snippet, StringComparer.Ordinal);
    }

    private static SearchResultItem ToSearchResult(
        SearchQueryRow row,
        string? markerStart,
        string? markerEnd)
    {
        if (row.MatchKind != (int)WikiSearchMatchKind.BodyFts
            || markerStart is null
            || markerEnd is null)
        {
            return new SearchResultItem
            {
                Title = row.Title,
                RedirectTo = row.RedirectTo,
                Snippet = row.Snippet,
                MatchKind = (WikiSearchMatchKind)row.MatchKind,
                Highlights = [],
            };
        }

        var (plainSnippet, highlights) = ParseFtsSnippet(row.Snippet, markerStart, markerEnd);
        return new SearchResultItem
        {
            Title = row.Title,
            RedirectTo = row.RedirectTo,
            Snippet = plainSnippet,
            MatchKind = WikiSearchMatchKind.BodyFts,
            Highlights = highlights,
        };
    }

    private static (string PlainSnippet, IReadOnlyList<WikiSearchHighlight> Highlights) ParseFtsSnippet(
        string snippet,
        string markerStart,
        string markerEnd)
    {
        var plain = new System.Text.StringBuilder(snippet.Length);
        var highlights = new List<WikiSearchHighlight>();
        var highlightStart = -1;
        var index = 0;

        while (index < snippet.Length)
        {
            if (snippet.AsSpan(index).StartsWith(markerStart, StringComparison.Ordinal))
            {
                if (highlightStart < 0)
                    highlightStart = plain.Length;
                index += markerStart.Length;
                continue;
            }

            if (snippet.AsSpan(index).StartsWith(markerEnd, StringComparison.Ordinal))
            {
                if (highlightStart >= 0)
                {
                    var length = plain.Length - highlightStart;
                    if (length > 0)
                        highlights.Add(new WikiSearchHighlight(highlightStart, length));
                    highlightStart = -1;
                }

                index += markerEnd.Length;
                continue;
            }

            plain.Append(snippet[index]);
            index++;
        }

        return (plain.ToString(), highlights);
    }

    private sealed class SearchQueryRow
    {
        public string Title { get; set; } = string.Empty;
        public string RedirectTo { get; set; } = string.Empty;
        public int MatchKind { get; set; }
        public double RankScore { get; set; }
        public string Snippet { get; set; } = string.Empty;
    }

    private sealed class PlainSnippetRow
    {
        public string Title { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
    }

    public async Task<int> GetFilteredCountAsync(
        string tableName,
        string searchColumn,
        string keyword,
        CancellationToken cancellationToken = default)
    {
        await Init(cancellationToken: cancellationToken);
        var likeTerm = $"%{keyword}%";
        var sql = $"SELECT COUNT(*) FROM {tableName} WHERE {searchColumn} LIKE ?";
        return await _db.ExecuteScalarAsync<int>(sql, likeTerm).WaitAsync(cancellationToken);
    }

    public Task<int> GetFilteredCountAsync<T>(
        string searchColumn,
        string keyword,
        CancellationToken cancellationToken = default)
        where T : new() =>
        GetFilteredCountAsync(typeof(T).Name, searchColumn, keyword, cancellationToken);

    public async Task<List<T>> GetFilteredPagedAsync<T>(
        string tableName,
        string selectColumns,
        string searchColumn,
        string keyword,
        string orderByColumn,
        int startIndex,
        int count,
        bool isDesc = true,
        CancellationToken cancellationToken = default)
        where T : new()
    {
        await Init(cancellationToken: cancellationToken);
        var likeTerm = $"%{keyword}%";
        var orderDirection = isDesc ? "DESC" : "ASC";
        var sql = $"""
            SELECT {selectColumns}
            FROM {tableName}
            WHERE {searchColumn} LIKE ?
            ORDER BY {orderByColumn} {orderDirection}
            LIMIT ? OFFSET ?
            """;
        return await _db.QueryAsync<T>(sql, likeTerm, count, startIndex).WaitAsync(cancellationToken);
    }

    public Task<List<T>> GetFilteredPagedAsync<T>(
        string searchColumn,
        string keyword,
        string orderByColumn,
        int startIndex,
        int count,
        bool isDesc = true,
        CancellationToken cancellationToken = default)
        where T : new() =>
        GetFilteredPagedAsync<T>(
            typeof(T).Name,
            "*",
            searchColumn,
            keyword,
            orderByColumn,
            startIndex,
            count,
            isDesc,
            cancellationToken);

    public async Task<List<AssetMetadata>> GetAllAssetsMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        await Init(cancellationToken: cancellationToken);
        const string sql = """
            SELECT
                FileName,
                length(Data) AS FileSize,
                LastModified
            FROM WikiAsset
            ORDER BY LastModified DESC
            """;
        return await _db.QueryAsync<AssetMetadata>(sql).WaitAsync(cancellationToken);
    }

    private static SQLiteAsyncConnection CreateConnection(string databasePath)
    {
        const SQLiteOpenFlags flags =
            SQLiteOpenFlags.ReadWrite |
            SQLiteOpenFlags.Create |
            SQLiteOpenFlags.SharedCache;
        return new SQLiteAsyncConnection(databasePath, flags);
    }

    private static void EnsureDatabaseDirectory(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    private async Task InitFtsTableCoreAsync(CancellationToken cancellationToken)
    {
        const string createSql = """
            CREATE VIRTUAL TABLE IF NOT EXISTS WikiSearchIndex USING fts5(
                Title UNINDEXED,
                PlainContent,
                tokenize='trigram'
            );
            """;
        await _db.ExecuteAsync(createSql).WaitAsync(cancellationToken);
    }

    private async Task MigrateWikiBookColumnsAsync(CancellationToken cancellationToken)
    {
        // These names and SQLite declarations intentionally match the legacy migration.
        var newColumns = new Dictionary<string, string>
        {
            ["ApiBaseUrl"] = "TEXT",
            ["BaseUrl"] = "TEXT",
            ["PageBaseUrl"] = "TEXT",
            ["RedirectListUrl"] = "TEXT",
            ["MainNamespace"] = "INTEGER DEFAULT 0",
            ["GuideNamespace"] = "INTEGER DEFAULT 0",
            ["AdditionalNamespaces"] = "TEXT",
            ["JunkXPath"] = "TEXT",
            ["DataFolder"] = "TEXT",
            ["DefaultPageContent"] = "TEXT",
            ["DefaultPageTitle"] = "TEXT",
        };

        var existingColumns = (await _db.QueryAsync<WikiBookColumnInfo>(
                "SELECT name AS Name FROM pragma_table_info('WikiBook')")
            .WaitAsync(cancellationToken))
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (columnName, columnType) in newColumns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existingColumns.Contains(columnName))
                continue;

            await _db.ExecuteAsync(
                    $"ALTER TABLE WikiBook ADD COLUMN {columnName} {columnType};")
                .WaitAsync(cancellationToken);
        }
    }

    private async Task SeedWikiBooksAsync(CancellationToken cancellationToken)
    {
        var defaultWikiBooks = CreateDefaultWikiBooks();
        var existingBooks = await _db.Table<WikiBook>().ToListAsync().WaitAsync(cancellationToken);
        var existingIds = existingBooks.Select(book => book.Id).ToHashSet();
        var missingBooks = defaultWikiBooks.Where(book => !existingIds.Contains(book.Id)).ToList();

        if (missingBooks.Count > 0)
            await _db.InsertAllAsync(missingBooks).WaitAsync(cancellationToken);

        var defaultBooksById = defaultWikiBooks.ToDictionary(book => book.Id);
        foreach (var existing in existingBooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!defaultBooksById.TryGetValue(existing.Id, out var defaults))
                continue;

            var needsUpdate = false;
            needsUpdate |= AssignIfDifferent(existing.Title, defaults.Title, value => existing.Title = value);
            needsUpdate |= AssignIfDifferent(existing.Description, defaults.Description, value => existing.Description = value);
            needsUpdate |= AssignIfDifferent(existing.BaseUrl, defaults.BaseUrl, value => existing.BaseUrl = value);
            needsUpdate |= AssignIfDifferent(existing.ApiBaseUrl, defaults.ApiBaseUrl, value => existing.ApiBaseUrl = value);
            needsUpdate |= AssignIfDifferent(existing.RedirectListUrl, defaults.RedirectListUrl, value => existing.RedirectListUrl = value);

            if (existing.MainNamespace != defaults.MainNamespace)
            {
                existing.MainNamespace = defaults.MainNamespace;
                needsUpdate = true;
            }

            needsUpdate |= AssignIfDifferent(existing.AdditionalNamespaces, defaults.AdditionalNamespaces, value => existing.AdditionalNamespaces = value);
            needsUpdate |= AssignIfDifferent(existing.JunkXPath, defaults.JunkXPath, value => existing.JunkXPath = value);
            needsUpdate |= AssignIfDifferent(existing.DataFolder, defaults.DataFolder, value => existing.DataFolder = value);
            needsUpdate |= AssignIfDifferent(existing.DefaultPageContent, defaults.DefaultPageContent, value => existing.DefaultPageContent = value);
            needsUpdate |= AssignIfDifferent(existing.DefaultPageTitle, defaults.DefaultPageTitle, value => existing.DefaultPageTitle = value);

            if (needsUpdate)
                await _db.UpdateAsync(existing).WaitAsync(cancellationToken);
        }
    }

    private async Task SeedWikiPageAsync(WikiBook? seedBook, CancellationToken cancellationToken)
    {
        var defaultTitle = seedBook?.DefaultPageTitle ?? seedBook?.Title;
        if (seedBook is null || defaultTitle is null)
            return;

        var exists = await ItemExistsWithoutInitAsync<WikiPage>(defaultTitle, cancellationToken);
        if (exists)
            return;

        var defaultPage = new WikiPage
        {
            Title = seedBook.DefaultPageTitle ?? seedBook.Title,
            Content = seedBook.DefaultPageContent ?? "请先下载数据",
            LastModified = DateTime.UnixEpoch,
        };
        await _db.InsertAsync(defaultPage).WaitAsync(cancellationToken);
    }

    private async Task<bool> ItemExistsWithoutInitAsync<T>(
        object primaryKey,
        CancellationToken cancellationToken)
        where T : new()
    {
        var mapping = _db.GetConnection().GetMapping(typeof(T));
        var pk = mapping.PK
            ?? throw new InvalidOperationException($"{typeof(T).Name} does not define a primary key.");
        var query = $"SELECT COUNT(*) FROM \"{mapping.TableName}\" WHERE \"{pk.Name}\" = ?";
        return await _db.ExecuteScalarAsync<int>(query, primaryKey).WaitAsync(cancellationToken) > 0;
    }

    private static bool AssignIfDifferent(string current, string replacement, Action<string> assign)
    {
        if (current == replacement)
            return false;

        assign(replacement);
        return true;
    }

    private sealed class WikiBookColumnInfo
    {
        public string Name { get; set; } = string.Empty;
    }

    private static List<WikiBook> CreateDefaultWikiBooks() =>
    [
        new()
        {
            Id = 1,
            Title = "泰拉瑞亚中文百科",
            Description = "《泰拉瑞亚》是冒险之地！是神秘之地！是可让你塑造、捍卫、享受的大地。在泰拉瑞亚，你有无穷选择。手指发痒的动作游戏迷？建筑大师？收藏家？探险家？每个人都能找到自己想要的。",
            ApiBaseUrl = "https://terraria.wiki.gg/zh/api.php",
            BaseUrl = "https://terraria.wiki.gg",
            RedirectListUrl = "/zh/wiki/Special:ListRedirects?limit=5000",
            MainNamespace = 0,
            AdditionalNamespaces = "10000",
            JunkXPath = "//div[@id='marker-for-new-portlet-link']|//span[@class='mw-editsection']|//comment()",
            DataFolder = "Terraria_Wiki_zh",
            DefaultPageContent = "请先下载数据",
            DefaultPageTitle = "Terraria Wiki",
        },
        new()
        {
            Id = 2,
            Title = "灾厄中文百科",
            Description = "灾厄模组是泰拉瑞亚的最大内容添加类模组，在原版毕业之后加入了数个小时的新流程，还有大量新敌怪和数量超越原版的新Boss。",
            BaseUrl = "https://calamity.huijiwiki.com",
            ApiBaseUrl = "https://calamity.huijiwiki.com/api.php",
            RedirectListUrl = "/wiki/%E7%89%B9%E6%AE%8A:%E9%87%8D%E5%AE%9A%E5%90%91%E9%A1%B5%E5%88%97%E8%A1%A8?limit=5000",
            MainNamespace = 0,
            AdditionalNamespaces = string.Empty,
            JunkXPath = "//span[@class='mw-editsection']|//comment()",
            DataFolder = "Calamity_Wiki_zh",
            DefaultPageContent = "请先下载数据",
            DefaultPageTitle = "首页",
        },
        new()
        {
            Id = 3,
            Title = "Terraria Wiki",
            Description = "Terraria is a land of adventure! A land of mystery! A land that's yours to shape, defend, and enjoy. Your options in Terraria are limitless. Are you an action gamer with an itchy trigger finger? A master builder? A collector? An explorer? There's something for everyone.",
            ApiBaseUrl = "https://terraria.wiki.gg/api.php",
            BaseUrl = "https://terraria.wiki.gg",
            RedirectListUrl = "/wiki/Special:ListRedirects?limit=5000",
            MainNamespace = 0,
            AdditionalNamespaces = "110",
            JunkXPath = "//div[@id='marker-for-new-portlet-link']|//span[@class='mw-editsection']|//comment()",
            DataFolder = "Terraria_Wiki_en",
            DefaultPageContent = "please download data first",
            DefaultPageTitle = "Terraria Wiki",
        },
        new()
        {
            Id = 4,
            Title = "Calamity Mod Wiki",
            Description = "The Calamity Mod is a large content mod for Terraria which adds many hours of endgame content and dozens of enemies and bosses dispersed throughout the vanilla game's progression. The Calamity Mod also features several harder difficulty modes, five new biomes and new structures, a new class, more than fifty new songs, over fifty recipes for previously uncraftable vanilla items, and other assorted changes to vanilla gameplay.",
            ApiBaseUrl = "https://calamitymod.wiki.gg/api.php",
            BaseUrl = "https://calamitymod.wiki.gg",
            RedirectListUrl = "/wiki/Special:ListRedirects?limit=5000",
            MainNamespace = 0,
            AdditionalNamespaces = "10000",
            JunkXPath = "//div[@id='marker-for-new-portlet-link']|//span[@class='mw-editsection']|//comment()",
            DataFolder = "Calamity_Wiki_en",
            DefaultPageContent = "please download data first",
            DefaultPageTitle = "Calamity Mod Wiki",
        },
    ];
}
