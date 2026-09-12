using SQLite;
using Terraria_Wiki.Core.Compatibility;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Data;

internal sealed class WikiFavoriteStoreException : Exception
{
    public WikiFavoriteStoreException(string code, string message, Exception? inner = null)
        : base(message, inner) => Code = code;

    public string Code { get; }
}

internal sealed record WikiFavoriteStoreReadResult(
    IReadOnlyList<WikiFavorite> Items,
    string? IssueCode = null);

internal static class WikiFavoriteStore
{
    public static IReadOnlyList<WikiFavorite> ReadAll(
        WikiDataPaths paths,
        CancellationToken cancellationToken = default) =>
        ReadAllWithStatus(paths, cancellationToken).Items;

    public static WikiFavoriteStoreReadResult ReadAllWithStatus(
        WikiDataPaths paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();
        var databasePath = ResolveExistingDatabase(paths);
        if (databasePath is null)
            return new WikiFavoriteStoreReadResult([]);

        try
        {
            using var connection = Open(databasePath, SQLiteOpenFlags.ReadOnly);
            if (!HasFavoritesTable(connection))
                return new WikiFavoriteStoreReadResult([], "favorite-table-not-found");
            cancellationToken.ThrowIfCancellationRequested();
            return new WikiFavoriteStoreReadResult(connection.Query<WikiFavorite>(
                "SELECT WikiTitle, FavoritedAt FROM WikiFavorite ORDER BY FavoritedAt DESC, WikiTitle COLLATE BINARY;"));
        }
        catch (WikiFavoriteStoreException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Error("favorite-database-unreadable", "The Wiki favorites could not be read.", exception);
        }
    }

    public static bool IsFavorite(
        WikiDataPaths paths,
        string title,
        CancellationToken cancellationToken = default)
    {
        ValidateTitle(title);
        cancellationToken.ThrowIfCancellationRequested();
        var databasePath = ResolveExistingDatabase(paths);
        if (databasePath is null)
            return false;
        try
        {
            using var connection = Open(databasePath, SQLiteOpenFlags.ReadOnly);
            if (!HasFavoritesTable(connection))
                return false;
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM WikiFavorite WHERE WikiTitle = ?;",
                title) != 0;
        }
        catch (WikiFavoriteStoreException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Error("favorite-database-unreadable", "The Wiki favorite state could not be read.", exception);
        }
    }

    public static bool SetFavorite(
        WikiDataPaths paths,
        string title,
        bool favorite,
        DateTime favoritedAt,
        CancellationToken cancellationToken = default)
    {
        ValidateTitle(title);
        cancellationToken.ThrowIfCancellationRequested();
        var databasePath = ResolveExistingDatabase(paths)
            ?? throw Error("favorite-database-not-found", "The Wiki content database does not exist.");
        try
        {
            using var connection = Open(databasePath, SQLiteOpenFlags.ReadWrite);
            if (!HasFavoritesTable(connection))
                throw Error("favorite-table-not-found", "The Wiki content database does not contain favorites.");
            connection.RunInTransaction(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var exists = connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM WikiFavorite WHERE WikiTitle = ?;",
                    title) != 0;
                if (favorite && !exists)
                {
                    connection.Insert(new WikiFavorite { WikiTitle = title, FavoritedAt = favoritedAt });
                }
                else if (!favorite && exists)
                {
                    connection.Execute("DELETE FROM WikiFavorite WHERE WikiTitle = ?;", title);
                }
            });
            return favorite;
        }
        catch (WikiFavoriteStoreException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Error("favorite-database-write-failed", "The Wiki favorite could not be updated.", exception);
        }
    }

    internal static string? ResolveExistingDatabase(WikiDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var directory = paths.DataDirectory;
        if (!Directory.Exists(directory))
            return null;
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw Error("data-directory-reparse-point", "A Wiki data directory cannot be a reparse point.");

        var current = paths.ContentDatabasePath;
        var legacy = Path.Combine(directory, LegacyWikiCompatibility.LegacyDatabaseName);
        var hasCurrent = File.Exists(current);
        var hasLegacy = File.Exists(legacy);
        if (hasCurrent && hasLegacy)
            throw Error("database-family-conflict", "Multiple Wiki content databases are present.");
        if (!hasCurrent && !hasLegacy)
        {
            if (HasSidecar(current) || HasSidecar(legacy))
                throw Error("orphan-database-sidecar", "A Wiki database sidecar has no matching database.");
            return null;
        }

        var selected = hasCurrent ? current : legacy;
        var other = hasCurrent ? legacy : current;
        if (HasSidecar(other))
            throw Error("orphan-database-sidecar", "A Wiki database sidecar belongs to another database family.");
        if ((File.GetAttributes(selected) & FileAttributes.ReparsePoint) != 0)
            throw Error("database-reparse-point", "A Wiki content database cannot be a reparse point.");
        return selected;
    }

    private static SQLiteConnection Open(string path, SQLiteOpenFlags mode) =>
        new(path, mode | SQLiteOpenFlags.FullMutex);

    private static bool HasFavoritesTable(SQLiteConnection connection)
    {
        var table = connection.Query<SchemaRow>(
            "SELECT type AS Type, sql AS Sql FROM sqlite_master WHERE name = 'WikiFavorite';")
            .SingleOrDefault();
        if (table is null)
            return false;
        if (!string.Equals(table.Type, "table", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(table.Sql))
            throw Error("favorite-table-invalid", "The Wiki favorites table is incompatible.");

        var columns = connection.Query<ColumnRow>("PRAGMA table_info('WikiFavorite');")
            .Select(row => row.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains("WikiTitle") || !columns.Contains("FavoritedAt"))
            throw Error("favorite-table-invalid", "The Wiki favorites table is incompatible.");
        return true;
    }

    private static bool HasSidecar(string databasePath) =>
        File.Exists(databasePath + "-wal") || File.Exists(databasePath + "-shm");

    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("A page title is required.", nameof(title));
    }

    private static WikiFavoriteStoreException Error(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    private sealed class SchemaRow
    {
        public string Type { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
    }

    private sealed class ColumnRow
    {
        public string Name { get; set; } = string.Empty;
    }
}
