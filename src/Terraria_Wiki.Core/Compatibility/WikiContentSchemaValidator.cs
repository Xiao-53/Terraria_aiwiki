using System.Text.RegularExpressions;
using SQLite;

namespace Terraria_Wiki.Core.Compatibility;

internal sealed class WikiContentSchemaValidationException : Exception
{
    public WikiContentSchemaValidationException(string code, string message, Exception? inner = null)
        : base(message, inner) => Code = code;

    public string Code { get; }
}

internal static class WikiContentSchemaValidator
{
    private static readonly IReadOnlyDictionary<string, SchemaRequirement> RequiredTables =
        new Dictionary<string, SchemaRequirement>(StringComparer.Ordinal)
        {
            ["WikiPage"] = new(["Title", "Content", "LastModified"], "Title", []),
            ["WikiRedirect"] = new(["FromName", "ToTarget"], "FromName", ["ToTarget"]),
            ["WikiHistory"] = new(["Id", "WikiTitle", "ReadAt", "DateKey"], "Id", ["WikiTitle", "DateKey"]),
            ["WikiFavorite"] = new(["WikiTitle", "FavoritedAt"], "WikiTitle", []),
            ["WikiAsset"] = new(["FileName", "Data", "MimeType", "LastModified"], "FileName", []),
        };

    public static void Validate(string databasePath)
    {
        try
        {
            using var connection = new SQLiteConnection(
                databasePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            var integrity = connection.ExecuteScalar<string>("PRAGMA integrity_check;");
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                throw Error("database-integrity-failed", $"SQLite integrity_check returned: {integrity}.");

            foreach (var (table, requirement) in RequiredTables)
                ValidateOrdinaryTable(connection, table, requirement);
            ValidateFtsTable(connection);
        }
        catch (WikiContentSchemaValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Error("database-validation-failed", "The database could not be validated.", exception);
        }
    }

    private static void ValidateOrdinaryTable(
        SQLiteConnection connection,
        string table,
        SchemaRequirement requirement)
    {
        var schemaObject = connection.FindWithQuery<SchemaObject>(
            "SELECT type AS Type, sql AS Sql FROM sqlite_master WHERE name = ?;",
            table);
        if (schemaObject is null
            || !string.Equals(schemaObject.Type, "table", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(schemaObject.Sql)
            || schemaObject.Sql.Contains("CREATE VIRTUAL TABLE", StringComparison.OrdinalIgnoreCase))
            throw Error("database-schema-invalid", $"The database does not contain the expected {table} table.");

        var quotedTable = QuoteIdentifier(table);
        var columns = connection.Query<SchemaColumn>($"PRAGMA table_info({quotedTable});");
        var names = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var primaryKeys = columns
            .Where(column => column.PrimaryKeyPosition > 0)
            .OrderBy(column => column.PrimaryKeyPosition)
            .Select(column => column.Name)
            .ToArray();
        if (requirement.Columns.Any(column => !names.Contains(column))
            || primaryKeys.Length != 1
            || !string.Equals(primaryKeys[0], requirement.PrimaryKey, StringComparison.OrdinalIgnoreCase))
            throw Error("database-schema-invalid", $"The database has an incompatible {table} schema.");

        var indexes = connection.Query<SchemaIndex>($"PRAGMA index_list({quotedTable});");
        foreach (var requiredIndexColumn in requirement.IndexColumns)
        {
            var found = indexes.Any(index =>
            {
                if (string.IsNullOrWhiteSpace(index.Name)) return false;
                var columnsInIndex = connection.Query<SchemaIndexColumn>(
                        $"PRAGMA index_info({QuoteIdentifier(index.Name)});")
                    .OrderBy(column => column.Sequence)
                    .Select(column => column.Name)
                    .ToArray();
                return columnsInIndex.Length == 1
                    && string.Equals(columnsInIndex[0], requiredIndexColumn, StringComparison.OrdinalIgnoreCase);
            });
            if (!found)
                throw Error(
                    "database-schema-invalid",
                    $"The database is missing the expected {table}.{requiredIndexColumn} index.");
        }
    }

    private static void ValidateFtsTable(SQLiteConnection connection)
    {
        var tableEntry = connection.Query<SchemaTableEntry>("PRAGMA table_list;")
            .SingleOrDefault(entry => string.Equals(entry.Name, "WikiSearchIndex", StringComparison.Ordinal));
        var schemaObject = connection.FindWithQuery<SchemaObject>(
            "SELECT type AS Type, sql AS Sql FROM sqlite_master WHERE name = 'WikiSearchIndex';");
        if (tableEntry is null
            || !string.Equals(tableEntry.Type, "virtual", StringComparison.OrdinalIgnoreCase)
            || schemaObject is null
            || !string.Equals(schemaObject.Type, "table", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(schemaObject.Sql))
            throw Error("database-schema-invalid", "The database does not contain the expected FTS5 virtual table.");

        var columns = connection.Query<SchemaColumn>("PRAGMA table_info(\"WikiSearchIndex\");")
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sqlWithoutComments = Regex.Replace(
            schemaObject.Sql,
            @"--[^\r\n]*|/\*.*?\*/",
            string.Empty,
            RegexOptions.Singleline);
        if (!columns.Contains("Title")
            || !columns.Contains("PlainContent")
            || !Regex.IsMatch(sqlWithoutComments, @"\bUSING\s+fts5\s*\(", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(
                sqlWithoutComments,
                @"\btokenize\s*=\s*(?:'trigram'|""trigram""|trigram)",
                RegexOptions.IgnoreCase))
            throw Error("database-schema-invalid", "The database does not contain the expected FTS5 trigram index.");

        // Ordinary tables can contain a misleading SQL comment; executing MATCH
        // proves that the object is an actual full-text virtual table.
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WikiSearchIndex WHERE WikiSearchIndex MATCH ?;",
            "phase01schemavalidationtoken");
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private static WikiContentSchemaValidationException Error(
        string code,
        string message,
        Exception? inner = null) =>
        new(code, message, inner);

    private sealed record SchemaRequirement(
        string[] Columns,
        string PrimaryKey,
        string[] IndexColumns);

    private sealed class SchemaObject
    {
        public string Type { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
    }

    private sealed class SchemaColumn
    {
        public string Name { get; set; } = string.Empty;

        [Column("pk")]
        public int PrimaryKeyPosition { get; set; }
    }

    private sealed class SchemaIndex
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class SchemaIndexColumn
    {
        [Column("seqno")]
        public int Sequence { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class SchemaTableEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
