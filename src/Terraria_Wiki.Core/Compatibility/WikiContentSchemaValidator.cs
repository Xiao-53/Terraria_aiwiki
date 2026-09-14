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
            ["WikiDatabaseMetadata"] = new(["Key", "Value"], "Key", []),
            ["WikiPage"] = new(["Title", "Content", "LastModified"], "Title", []),
            ["WikiRedirect"] = new(["FromName", "ToTarget"], "FromName", ["ToTarget"]),
            ["WikiFavorite"] = new(["WikiTitle", "FavoritedAt"], "WikiTitle", []),
            ["WikiAsset"] = new(["FileName", "Data", "MimeType", "LastModified"], "FileName", []),
            ["WikiSearchSection"] = new(
                ["Id", "PageTitle", "SectionOrdinal", "Title", "HeadingPath", "Anchor"],
                "Id",
                ["PageTitle"]),
            ["WikiSearchRegion"] = new(
                ["Id", "PageTitle", "SectionOrdinal", "RegionOrdinal", "Kind", "HeadingPath", "Anchor", "ContextText", "BodyText"],
                "Id",
                ["PageTitle"]),
        };

    public static void Validate(string databasePath, int? expectedWikiId = null)
    {
        try
        {
            using var connection = new SQLiteConnection(
                databasePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            var integrity = connection.ExecuteScalar<string>("PRAGMA integrity_check;");
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                throw Error("database-integrity-failed", $"SQLite integrity_check returned: {integrity}.");

            var metadataTable = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'WikiDatabaseMetadata';");
            if (metadataTable == 0)
                throw Error("database-format-unsupported", "The database does not declare the supported content format.");

            foreach (var (table, requirement) in RequiredTables)
                ValidateOrdinaryTable(connection, table, requirement);
            ValidateMetadata(connection, expectedWikiId);
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
            .SingleOrDefault(entry => string.Equals(entry.Name, "WikiSearchRegionFts", StringComparison.Ordinal));
        var schemaObject = connection.FindWithQuery<SchemaObject>(
            "SELECT type AS Type, sql AS Sql FROM sqlite_master WHERE name = 'WikiSearchRegionFts';");
        if (tableEntry is null
            || !string.Equals(tableEntry.Type, "virtual", StringComparison.OrdinalIgnoreCase)
            || schemaObject is null
            || !string.Equals(schemaObject.Type, "table", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(schemaObject.Sql))
            throw Error("database-schema-invalid", "The database does not contain the expected FTS5 virtual table.");

        var columns = connection.Query<SchemaColumn>("PRAGMA table_info(\"WikiSearchRegionFts\");")
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sqlWithoutComments = Regex.Replace(
            schemaObject.Sql,
            @"--[^\r\n]*|/\*.*?\*/",
            string.Empty,
            RegexOptions.Singleline);
        if (!columns.Contains("ContextTokens")
            || !columns.Contains("BodyTokens")
            || !Regex.IsMatch(sqlWithoutComments, @"\bUSING\s+fts5\s*\(", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(sqlWithoutComments, "\\bcontent\\s*=\\s*(?:''|\"\")", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(
                sqlWithoutComments,
                @"\btokenize\s*=\s*(?:'unicode61[^']*'|""unicode61[^""]*"")",
                RegexOptions.IgnoreCase)
            || !Regex.IsMatch(sqlWithoutComments, "\\bdetail\\s*=\\s*(?:'full'|\"full\")", RegexOptions.IgnoreCase))
            throw Error("database-schema-invalid", "The database does not contain the expected FTS5 region index.");

        // Ordinary tables can contain a misleading SQL comment; executing MATCH
        // proves that the object is an actual full-text virtual table.
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WikiSearchRegionFts WHERE WikiSearchRegionFts MATCH ?;",
            "formatvalidationtoken");
        var regionCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WikiSearchRegion;");
        var ftsCount = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WikiSearchRegionFts;");
        var orphanCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WikiSearchRegion AS region LEFT JOIN WikiPage AS page ON page.Title = region.PageTitle WHERE page.Title IS NULL;");
        var orphanSectionCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WikiSearchSection AS section LEFT JOIN WikiPage AS page ON page.Title = section.PageTitle WHERE page.Title IS NULL;");
        var missingRegionSectionCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WikiSearchRegion AS region LEFT JOIN WikiSearchSection AS section ON section.PageTitle = region.PageTitle AND section.SectionOrdinal = region.SectionOrdinal WHERE section.Id IS NULL;");
        var missingPageSectionCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM WikiPage AS page LEFT JOIN WikiSearchSection AS section ON section.PageTitle = page.Title WHERE section.Id IS NULL;");
        if (regionCount != ftsCount || orphanCount != 0 || orphanSectionCount != 0
            || missingRegionSectionCount != 0 || missingPageSectionCount != 0)
            throw Error("database-search-index-invalid", "The region search index is incomplete or inconsistent.");
    }

    private static void ValidateMetadata(SQLiteConnection connection, int? expectedWikiId)
    {
        var version = connection.ExecuteScalar<string>(
            "SELECT Value FROM WikiDatabaseMetadata WHERE Key = 'format_version';");
        var extractorVersion = connection.ExecuteScalar<string>(
            "SELECT Value FROM WikiDatabaseMetadata WHERE Key = 'extractor_version';");
        var wikiId = connection.ExecuteScalar<string>(
            "SELECT Value FROM WikiDatabaseMetadata WHERE Key = 'wiki_id';");
        if (version != "2" || extractorVersion != "1"
            || !int.TryParse(wikiId, out var parsedWikiId) || parsedWikiId <= 0)
            throw Error("database-format-unsupported", "The database does not declare the supported content format and Wiki identity.");
        if (expectedWikiId.HasValue && parsedWikiId != expectedWikiId.Value)
            throw Error("database-wiki-mismatch", "The database belongs to a different Wiki identity.");
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
