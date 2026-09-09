using System.Security.Cryptography;
using SQLite;

namespace Phase00.Probes;

/// <summary>
/// Characterizes the existing SQLite/FTS5 shape without opening a production
/// database. A database and any existing -wal/-shm sidecars are first copied
/// as one stable input into a local snapshot. Every SQLite operation after
/// that point is performed against the snapshot or its writable copy.
/// </summary>
public static class SqliteProbe
{
    private const int MaxSnapshotAttempts = 5;

    public static async Task<SqliteProbeResult> ProbeAsync(
        string databasePath,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(databasePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("SQLite database was not found.", sourcePath);

        var workRoot = Path.GetFullPath(workingDirectory);
        if (IsPathWithin(workRoot, sourcePath))
            throw new InvalidOperationException("Database input cannot be inside the working/output directory.");

        Directory.CreateDirectory(workRoot);
        var snapshot = await CaptureConsistentSnapshotAsync(
            sourcePath,
            Path.Combine(workRoot, "snapshot"),
            cancellationToken);

        // Checkpoint only the local snapshot. This folds a copied WAL into the
        // copied database before the read-only characterization and writable
        // copy are opened; the source is never passed to SQLite.
        var checkpoint = await CheckpointSnapshotAsync(snapshot.DatabasePath, cancellationToken);
        snapshot = snapshot with
        {
            Files = await DescribeSnapshotFilesAsync(
                snapshot.Descriptors
                    .Select(x => new SqliteFileDescriptor(x.Role, x.SourcePath, x.SnapshotPath))
                    .ToArray(),
                cancellationToken),
            Checkpoint = checkpoint
        };

        var readOnly = await InspectReadOnlyAsync(snapshot.DatabasePath, cancellationToken);

        var writableDirectory = Path.Combine(workRoot, "writable");
        var writablePath = await CopySnapshotToWritableAsync(snapshot, writableDirectory, cancellationToken);
        var writable = await ExerciseWritableCopyAsync(
            writablePath,
            workRoot,
            readOnly.Tables,
            cancellationToken);

        var sourceDatabase = snapshot.SourceFiles.Single(file => file.Role == "database");
        return new SqliteProbeResult(
            sourcePath,
            sourceDatabase.Sha256Before,
            sourceDatabase.Sha256After,
            snapshot.SourceFilesUnchanged,
            writable.DatabasePath,
            writable.Sha256Before,
            writable.Sha256After,
            readOnly,
            writable,
            snapshot);
    }

    /// <summary>
    /// Copies the database and its sidecars without opening the source with
    /// SQLite. The input is accepted only when every present/absent file has
    /// matching before/after state. A source that keeps changing fails closed
    /// instead of yielding a potentially mixed database/WAL pair.
    /// </summary>
    public static async Task<SqliteSnapshotEvidence> CaptureConsistentSnapshotAsync(
        string databasePath,
        string snapshotDirectory,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(databasePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("SQLite database was not found.", sourcePath);

        var snapshotRoot = Path.GetFullPath(snapshotDirectory);
        if (IsPathWithin(snapshotRoot, sourcePath))
            throw new InvalidOperationException("Snapshot directory cannot contain the source database.");

        Directory.CreateDirectory(snapshotRoot);
        var descriptors = CreateDescriptors(sourcePath, snapshotRoot);
        Exception? lastCopyError = null;

        for (var attempt = 1; attempt <= MaxSnapshotAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = await DescribeSourceFilesAsync(descriptors, cancellationToken);
            if (!before[0].PresentAfter)
                throw new FileNotFoundException("SQLite database was not found.", sourcePath);

            ClearDirectoryContents(snapshotRoot);
            try
            {
                foreach (var descriptor in descriptors)
                {
                    var state = before.Single(file => file.Role == descriptor.Role);
                    if (state.PresentAfter)
                        await CopyFileAsync(descriptor.SourcePath, descriptor.SnapshotPath, cancellationToken);
                }
            }
            catch (FileNotFoundException ex)
            {
                // A sidecar can be created or removed by SQLite while it is
                // active. Treat that as a changed input and retry together.
                lastCopyError = ex;
            }
            catch (DirectoryNotFoundException ex)
            {
                lastCopyError = ex;
            }

            var after = await DescribeSourceFilesAsync(descriptors, cancellationToken);
            var sourceStable = AreSourceStatesStable(before, after);
            var snapshotFiles = await DescribeSnapshotFilesAsync(descriptors, cancellationToken);
            var snapshotMatches = SnapshotMatchesSource(snapshotFiles, after);

            if (lastCopyError is null && sourceStable && snapshotMatches)
            {
                var sourceEvidence = MergeSourceStates(before, after);
                return new SqliteSnapshotEvidence(
                    snapshotRoot,
                    descriptors[0].SnapshotPath,
                    attempt,
                    sourceStable,
                    after.Any(file => file.Role == "wal" && file.PresentAfter),
                    after.Any(file => file.Role == "shm" && file.PresentAfter),
                    sourceEvidence,
                    snapshotFiles,
                    descriptors.Select(x => new SqliteSnapshotDescriptorEvidence(x.Role, x.SourcePath, x.SnapshotPath)).ToArray(),
                    null);
            }

            lastCopyError = null;
            if (attempt < MaxSnapshotAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt), cancellationToken);
        }

        throw new IOException(
            $"SQLite source changed while taking a consistent .db/.db-wal/.db-shm snapshot after {MaxSnapshotAttempts} attempts.",
            lastCopyError);
    }

    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1024 * 1024,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task CreateFixtureAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        if (File.Exists(fullPath + "-wal")) File.Delete(fullPath + "-wal");
        if (File.Exists(fullPath + "-shm")) File.Delete(fullPath + "-shm");

        await Task.Run(() =>
        {
            using var connection = new SQLiteConnection(
                fullPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            CreateSchema(connection);
            connection.Execute(
                "INSERT INTO WikiPage (Title, Content, LastModified) VALUES (?, ?, ?)",
                "Terraria Wiki",
                "<p>Phase 00 home. <a data-wiki=\"Alias\">Alias</a> <img src=\"/src/fixture.png\" /></p>",
                "2026-08-20T00:00:00Z");
            connection.Execute(
                "INSERT INTO WikiPage (Title, Content, LastModified) VALUES (?, ?, ?)",
                "Guide",
                "<p>Use a Copper Pickaxe. <a href=\"/wiki/Redirected\">link</a></p>",
                "2026-08-20T00:00:00Z");
            connection.Execute(
                "INSERT INTO WikiPage (Title, Content, LastModified) VALUES (?, ?, ?)",
                "Redirected",
                "<p>Redirect target with <img src=\"/src/fixture.png\" /></p>",
                "2026-08-20T00:00:01Z");
            connection.Execute(
                "INSERT INTO WikiRedirect (FromName, ToTarget) VALUES (?, ?)",
                "Alias",
                "Redirected#Section");
            connection.Execute(
                "INSERT INTO WikiSearchIndex (Title, PlainContent) VALUES (?, ?)",
                "Guide",
                "Use a Copper Pickaxe");
            connection.Execute(
                "INSERT INTO WikiSearchIndex (Title, PlainContent) VALUES (?, ?)",
                "Terraria Wiki",
                "Phase 00 home Alias");
            connection.Execute(
                "INSERT INTO WikiSearchIndex (Title, PlainContent) VALUES (?, ?)",
                "Redirected",
                "Redirect target Section");
            connection.Execute(
                "INSERT INTO WikiAsset (FileName, Data, MimeType, LastModified) VALUES (?, ?, ?, ?)",
                "fixture.png",
                FixturePng,
                "image/png",
                "2026-08-20T00:00:00Z");
            connection.Execute(
                "INSERT INTO WikiHistory (WikiTitle, ReadAt, DateKey) VALUES (?, ?, ?)",
                "Guide",
                "2026-08-20T00:00:00Z",
                "2026-08-20");
            connection.Execute(
                "INSERT INTO WikiFavorite (WikiTitle, CreatedAt) VALUES (?, ?)",
                "Guide",
                "2026-08-20T00:00:00Z");
        }, cancellationToken);
    }

    public static void CreateSchema(SQLiteConnection connection)
    {
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS WikiPage (Title TEXT PRIMARY KEY NOT NULL, Content TEXT NOT NULL, LastModified TEXT NOT NULL)");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS WikiRedirect (FromName TEXT PRIMARY KEY NOT NULL, ToTarget TEXT NOT NULL)");
        connection.Execute(
            "CREATE VIRTUAL TABLE IF NOT EXISTS WikiSearchIndex USING fts5(Title UNINDEXED, PlainContent, tokenize='trigram')");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS WikiAsset (FileName TEXT PRIMARY KEY NOT NULL, Data BLOB NOT NULL, MimeType TEXT NOT NULL, LastModified TEXT NULL)");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS WikiHistory (Id INTEGER PRIMARY KEY AUTOINCREMENT, WikiTitle TEXT NOT NULL, ReadAt TEXT NOT NULL, DateKey TEXT NOT NULL)");
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS WikiFavorite (Id INTEGER PRIMARY KEY AUTOINCREMENT, WikiTitle TEXT NOT NULL, CreatedAt TEXT NOT NULL)");
    }

    private static async Task<SqliteReadOnlyEvidence> InspectReadOnlyAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            using var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            var sqliteVersion = connection.ExecuteScalar<string>("SELECT sqlite_version()") ?? string.Empty;
            var compileOptions = connection.Query<PragmaValue>(
                    "SELECT compile_options AS Value FROM pragma_compile_options")
                .Select(x => x.Value ?? string.Empty)
                .Where(x => x.Length > 0)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            var tables = ReadTableNames(connection);
            var ftsSql = connection.ExecuteScalar<string>(
                    "SELECT sql FROM sqlite_master WHERE name='WikiSearchIndex'")
                ?? string.Empty;
            var ftsTrigram = ftsSql.Contains("trigram", StringComparison.OrdinalIgnoreCase);
            var fts5Enabled = compileOptions.Any(x => x.Contains("ENABLE_FTS5", StringComparison.OrdinalIgnoreCase))
                || ftsSql.Contains("fts5", StringComparison.OrdinalIgnoreCase);

            var pages = ReadPages(connection);
            var redirects = ReadRedirects(connection);
            var assets = ReadAssets(connection);
            var search = QuerySearch(connection, "pickaxe", 10);
            var pagination = QueryPageTitles(connection, 1, 1);

            return new SqliteReadOnlyEvidence(
                sqliteVersion,
                compileOptions,
                tables,
                fts5Enabled,
                ftsTrigram,
                ftsSql,
                pages,
                redirects,
                assets,
                search,
                pagination);
        }, cancellationToken);
    }

    private static async Task<SqliteWritableEvidence> ExerciseWritableCopyAsync(
        string copyPath,
        string workRoot,
        IReadOnlyList<string> sourceTables,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var copyHashBefore = ComputeSha256Async(copyPath).GetAwaiter().GetResult();
            using var connection = new SQLiteConnection(copyPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            var tableName = "Phase00ProbeScratch";
            connection.Execute($"DROP TABLE IF EXISTS [{tableName}]");
            connection.Execute($"CREATE TABLE [{tableName}] (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
            connection.Execute($"INSERT INTO [{tableName}] (Value) VALUES (?)", "before");
            var inserted = connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM [{tableName}]");
            connection.Execute($"UPDATE [{tableName}] SET Value=? WHERE Id=1", "after");
            var updated = connection.ExecuteScalar<string>($"SELECT Value FROM [{tableName}] WHERE Id=1") ?? string.Empty;
            connection.Execute($"DELETE FROM [{tableName}] WHERE Id=1");
            var deleted = connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM [{tableName}]") == 0;
            connection.Execute($"DROP TABLE [{tableName}]");

            var backupPath = Path.Combine(workRoot, "database-backup.db");
            if (File.Exists(backupPath)) File.Delete(backupPath);
            // VACUUM INTO is SQLite's portable online backup primitive and does
            // not require sqlite-net's optional native BackupAsync wrapper.
            connection.Execute("VACUUM INTO ?", backupPath);
            var backupBytes = new FileInfo(backupPath).Length;
            var backupReadable = false;
            var backupTables = Array.Empty<string>();
            try
            {
                using var backupConnection = new SQLiteConnection(
                    backupPath,
                    SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
                backupTables = ReadTableNames(backupConnection).ToArray();
                backupReadable = true;
            }
            catch (SQLiteException)
            {
                // Keep the failure in evidence instead of turning a schema
                // shape mismatch into a WikiPage-specific assertion.
            }

            var sourceTableSet = new HashSet<string>(sourceTables, StringComparer.OrdinalIgnoreCase);
            var backupTableSet = new HashSet<string>(backupTables, StringComparer.OrdinalIgnoreCase);
            var preservesSourceTables = sourceTableSet.All(backupTableSet.Contains);
            var copyHashAfter = ComputeSha256Async(copyPath).GetAwaiter().GetResult();

            return new SqliteWritableEvidence(
                copyPath,
                copyHashBefore,
                copyHashAfter,
                !string.Equals(copyHashBefore, copyHashAfter, StringComparison.OrdinalIgnoreCase),
                inserted == 1,
                updated == "after",
                deleted,
                backupPath,
                backupBytes,
                backupReadable,
                sourceTables.ToArray(),
                backupTables,
                preservesSourceTables);
        }, cancellationToken);
    }

    public static IReadOnlyList<SqlitePageEvidence> ReadPages(SQLiteConnection connection)
    {
        if (!HasColumns(connection, "WikiPage", "Title", "Content", "LastModified"))
            return Array.Empty<SqlitePageEvidence>();
        return connection.Query<PageRow>(
                "SELECT Title, length(CAST(Content AS BLOB)) AS ContentBytes, LastModified FROM WikiPage ORDER BY Title LIMIT 10")
            .Select(x => new SqlitePageEvidence(x.Title ?? string.Empty, x.ContentBytes, x.LastModified ?? string.Empty))
            .ToArray();
    }

    public static IReadOnlyList<SqliteRedirectEvidence> ReadRedirects(SQLiteConnection connection)
    {
        if (!HasColumns(connection, "WikiRedirect", "FromName", "ToTarget"))
            return Array.Empty<SqliteRedirectEvidence>();
        return connection.Query<RedirectRow>(
                "SELECT FromName, ToTarget FROM WikiRedirect ORDER BY FromName LIMIT 10")
            .Select(x => new SqliteRedirectEvidence(x.FromName ?? string.Empty, x.ToTarget ?? string.Empty))
            .ToArray();
    }

    public static IReadOnlyList<SqliteAssetEvidence> ReadAssets(SQLiteConnection connection)
    {
        if (!HasColumns(connection, "WikiAsset", "FileName", "Data", "MimeType"))
            return Array.Empty<SqliteAssetEvidence>();
        return connection.Query<AssetRow>(
                "SELECT FileName, length(Data) AS DataBytes, MimeType FROM WikiAsset ORDER BY FileName LIMIT 10")
            .Select(x => new SqliteAssetEvidence(x.FileName ?? string.Empty, x.DataBytes, x.MimeType ?? string.Empty))
            .ToArray();
    }

    public static IReadOnlyList<SqliteSearchEvidence> QuerySearch(
        SQLiteConnection connection,
        string query,
        int limit)
    {
        if (!HasColumns(connection, "WikiSearchIndex", "Title", "PlainContent"))
            return Array.Empty<SqliteSearchEvidence>();
        try
        {
            return connection.Query<SearchRow>(
                    "SELECT Title, length(CAST(PlainContent AS BLOB)) AS PlainContentBytes FROM WikiSearchIndex WHERE WikiSearchIndex MATCH ? LIMIT ?",
                    query,
                    Math.Clamp(limit, 1, 100))
                .Select(x => new SqliteSearchEvidence(x.Title ?? string.Empty, x.PlainContentBytes))
                .ToArray();
        }
        catch (SQLiteException)
        {
            return Array.Empty<SqliteSearchEvidence>();
        }
    }

    public static IReadOnlyList<string> QueryPageTitles(SQLiteConnection connection, int limit, int offset)
    {
        if (!HasColumns(connection, "WikiPage", "Title"))
            return Array.Empty<string>();
        return connection.Query<PageTitleRow>(
                "SELECT Title FROM WikiPage ORDER BY Title LIMIT ? OFFSET ?",
                Math.Clamp(limit, 1, 1000),
                Math.Max(offset, 0))
            .Select(x => x.Title ?? string.Empty)
            .ToArray();
    }

    private static async Task<SqliteCheckpointEvidence> CheckpointSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            using var connection = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            var row = connection.Query<CheckpointRow>("PRAGMA wal_checkpoint(TRUNCATE)").FirstOrDefault()
                ?? new CheckpointRow();
            return new SqliteCheckpointEvidence(
                true,
                row.Busy,
                row.Log,
                row.Checkpointed);
        }, cancellationToken);
    }

    private static async Task<string> CopySnapshotToWritableAsync(
        SqliteSnapshotEvidence snapshot,
        string writableDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(writableDirectory);
        ClearDirectoryContents(writableDirectory);
        var writableDatabasePath = Path.Combine(writableDirectory, Path.GetFileName(snapshot.DatabasePath));
        foreach (var file in snapshot.Files)
        {
            var destination = Path.Combine(writableDirectory, Path.GetFileName(file.Path));
            await CopyFileAsync(file.Path, destination, cancellationToken);
        }
        return writableDatabasePath;
    }

    private static async Task<IReadOnlyList<SqliteSourceFileEvidence>> DescribeSourceFilesAsync(
        IReadOnlyList<SqliteFileDescriptor> descriptors,
        CancellationToken cancellationToken)
    {
        var result = new List<SqliteSourceFileEvidence>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            var state = await DescribeSourceFileAsync(descriptor, cancellationToken);
            result.Add(state);
        }
        return result;
    }

    private static async Task<SqliteSourceFileEvidence> DescribeSourceFileAsync(
        SqliteFileDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(descriptor.SourcePath))
            return MissingSourceFile(descriptor);

        try
        {
            var (hash, length, stableRead) = await HashAndLengthAsync(descriptor.SourcePath, cancellationToken);
            return new SqliteSourceFileEvidence(
                descriptor.Role,
                descriptor.SourcePath,
                true,
                true,
                length,
                length,
                hash,
                hash,
                stableRead,
                descriptor.SnapshotPath);
        }
        catch (FileNotFoundException)
        {
            return MissingSourceFile(descriptor);
        }
        catch (DirectoryNotFoundException)
        {
            return MissingSourceFile(descriptor);
        }
    }

    private static SqliteSourceFileEvidence MissingSourceFile(SqliteFileDescriptor descriptor) =>
        new(
            descriptor.Role,
            descriptor.SourcePath,
            false,
            false,
            0,
            0,
            string.Empty,
            string.Empty,
            true,
            descriptor.SnapshotPath);

    private static async Task<(string Hash, long Length, bool StableRead)> HashAndLengthAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1024 * 1024,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        var initialLength = stream.Length;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var finalLength = stream.Length;
        return (Convert.ToHexString(hash).ToLowerInvariant(), finalLength, initialLength == finalLength);
    }

    private static async Task<IReadOnlyList<SqliteSnapshotFileEvidence>> DescribeSnapshotFilesAsync(
        IReadOnlyList<SqliteFileDescriptor> descriptors,
        CancellationToken cancellationToken)
    {
        var result = new List<SqliteSnapshotFileEvidence>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            if (!File.Exists(descriptor.SnapshotPath))
                continue;
            var (hash, length, stableRead) = await HashAndLengthAsync(descriptor.SnapshotPath, cancellationToken);
            if (!stableRead)
                throw new IOException($"Snapshot file changed while hashing: {descriptor.SnapshotPath}");
            result.Add(new SqliteSnapshotFileEvidence(
                descriptor.Role,
                descriptor.SnapshotPath,
                length,
                hash));
        }
        return result;
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            1024 * 1024,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static bool SnapshotMatchesSource(
        IReadOnlyList<SqliteSnapshotFileEvidence> snapshot,
        IReadOnlyList<SqliteSourceFileEvidence> source)
    {
        foreach (var sourceFile in source)
        {
            var copied = snapshot.SingleOrDefault(file => file.Role == sourceFile.Role);
            if (!sourceFile.PresentAfter)
            {
                if (copied is not null)
                    return false;
                continue;
            }

            if (copied is null
                || copied.Length != sourceFile.LengthAfter
                || !string.Equals(copied.Sha256, sourceFile.Sha256After, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static bool AreSourceStatesStable(
        IReadOnlyList<SqliteSourceFileEvidence> before,
        IReadOnlyList<SqliteSourceFileEvidence> after)
    {
        return before.Count == after.Count
            && before.Zip(after).All(pair =>
                pair.First.Role == pair.Second.Role
                && pair.First.PresentAfter == pair.Second.PresentAfter
                && pair.First.LengthAfter == pair.Second.LengthAfter
                && string.Equals(pair.First.Sha256After, pair.Second.Sha256After, StringComparison.OrdinalIgnoreCase)
                && pair.First.Unchanged
                && pair.Second.Unchanged);
    }

    private static IReadOnlyList<SqliteSourceFileEvidence> MergeSourceStates(
        IReadOnlyList<SqliteSourceFileEvidence> before,
        IReadOnlyList<SqliteSourceFileEvidence> after)
    {
        return before.Zip(after)
            .Select(pair => new SqliteSourceFileEvidence(
                pair.First.Role,
                pair.First.SourcePath,
                pair.First.PresentAfter,
                pair.Second.PresentAfter,
                pair.First.LengthAfter,
                pair.Second.LengthAfter,
                pair.First.Sha256After,
                pair.Second.Sha256After,
                pair.First.Unchanged && pair.Second.Unchanged
                    && pair.First.PresentAfter == pair.Second.PresentAfter
                    && pair.First.LengthAfter == pair.Second.LengthAfter
                    && string.Equals(pair.First.Sha256After, pair.Second.Sha256After, StringComparison.OrdinalIgnoreCase),
                pair.First.SnapshotPath))
            .ToArray();
    }

    private static SqliteFileDescriptor[] CreateDescriptors(string sourcePath, string snapshotRoot)
    {
        var sourceFileName = Path.GetFileName(sourcePath);
        return
        [
            new("database", sourcePath, Path.Combine(snapshotRoot, sourceFileName)),
            new("wal", sourcePath + "-wal", Path.Combine(snapshotRoot, sourceFileName + "-wal")),
            new("shm", sourcePath + "-shm", Path.Combine(snapshotRoot, sourceFileName + "-shm"))
        ];
    }

    private static IReadOnlyList<string> ReadTableNames(SQLiteConnection connection) =>
        connection.Query<PragmaValue>(
                "SELECT name AS Value FROM sqlite_master WHERE type IN ('table','virtual table') ORDER BY name")
            .Select(x => x.Value ?? string.Empty)
            .Where(x => x.Length > 0)
            .ToArray();

    private static bool HasColumns(SQLiteConnection connection, string tableName, params string[] columns)
    {
        if (columns.Length == 0)
            return HasTable(connection, tableName);
        if (!HasTable(connection, tableName))
            return false;
        var actual = connection.Query<ColumnRow>($"PRAGMA table_info([{tableName}])")
            .Select(x => x.Name ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return columns.All(actual.Contains);
    }

    private static bool HasTable(SQLiteConnection connection, string tableName) =>
        connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table','virtual table') AND name=?",
            tableName) == 1;

    private static void ClearDirectoryContents(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (Directory.Exists(entry))
                Directory.Delete(entry, recursive: true);
            else
                File.Delete(entry);
        }
    }

    private static bool IsPathWithin(string parent, string child)
    {
        var fullParent = Path.GetFullPath(parent);
        var root = EnsureTrailingSeparator(fullParent);
        var candidate = Path.GetFullPath(child);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, fullParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var separator = Path.DirectorySeparatorChar.ToString();
        return path.EndsWith(separator, StringComparison.Ordinal) ? path : path + separator;
    }

    private static readonly byte[] FixturePng =
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private sealed class PragmaValue { public string? Value { get; set; } }
    private sealed class ColumnRow { public string? Name { get; set; } }
    private sealed class PageRow { public string? Title { get; set; } public long ContentBytes { get; set; } public string? LastModified { get; set; } }
    private sealed class PageTitleRow { public string? Title { get; set; } }
    private sealed class RedirectRow { public string? FromName { get; set; } public string? ToTarget { get; set; } }
    private sealed class AssetRow { public string? FileName { get; set; } public long DataBytes { get; set; } public string? MimeType { get; set; } }
    private sealed class SearchRow { public string? Title { get; set; } public long PlainContentBytes { get; set; } }
    private sealed class CheckpointRow { public int Busy { get; set; } public int Log { get; set; } public int Checkpointed { get; set; } }
    private sealed record SqliteFileDescriptor(string Role, string SourcePath, string SnapshotPath);
}

public sealed record SqliteProbeResult(
    string SourcePath,
    string SourceSha256Before,
    string SourceSha256After,
    bool SourceUnchanged,
    string TemporaryCopyPath,
    string TemporaryCopySha256Before,
    string TemporaryCopySha256After,
    SqliteReadOnlyEvidence ReadOnly,
    SqliteWritableEvidence Writable,
    SqliteSnapshotEvidence Snapshot)
{
    public bool Success => SourceUnchanged
        && Snapshot.SourceFilesUnchanged
        && Snapshot.Checkpoint?.Completed == true
        && Writable.CrudInsert
        && Writable.CrudUpdate
        && Writable.CrudDelete
        && Writable.BackupBytes > 0
        && Writable.BackupReadable
        && Writable.BackupPreservesSourceTables;
}

public sealed record SqliteSnapshotEvidence(
    string DirectoryPath,
    string DatabasePath,
    int Attempts,
    bool SourceFilesUnchanged,
    bool IncludesWal,
    bool IncludesShm,
    IReadOnlyList<SqliteSourceFileEvidence> SourceFiles,
    IReadOnlyList<SqliteSnapshotFileEvidence> Files,
    IReadOnlyList<SqliteSnapshotDescriptorEvidence> Descriptors,
    SqliteCheckpointEvidence? Checkpoint);

public sealed record SqliteSourceFileEvidence(
    string Role,
    string SourcePath,
    bool PresentBefore,
    bool PresentAfter,
    long LengthBefore,
    long LengthAfter,
    string Sha256Before,
    string Sha256After,
    bool Unchanged,
    string SnapshotPath);

public sealed record SqliteSnapshotFileEvidence(
    string Role,
    string Path,
    long Length,
    string Sha256);

public sealed record SqliteSnapshotDescriptorEvidence(
    string Role,
    string SourcePath,
    string SnapshotPath);

public sealed record SqliteCheckpointEvidence(
    bool Completed,
    int BusyPages,
    int WalPages,
    int CheckpointedPages);

public sealed record SqliteReadOnlyEvidence(
    string SqliteVersion,
    IReadOnlyList<string> CompileOptions,
    IReadOnlyList<string> Tables,
    bool Fts5Enabled,
    bool FtsTrigram,
    string FtsSql,
    IReadOnlyList<SqlitePageEvidence> Pages,
    IReadOnlyList<SqliteRedirectEvidence> Redirects,
    IReadOnlyList<SqliteAssetEvidence> Assets,
    IReadOnlyList<SqliteSearchEvidence> SearchResults,
    IReadOnlyList<string> PaginationSample);

public sealed record SqliteWritableEvidence(
    string DatabasePath,
    string Sha256Before,
    string Sha256After,
    bool HashChanged,
    bool CrudInsert,
    bool CrudUpdate,
    bool CrudDelete,
    string BackupPath,
    long BackupBytes,
    bool BackupReadable,
    IReadOnlyList<string> SourceTables,
    IReadOnlyList<string> BackupTables,
    bool BackupPreservesSourceTables);

public sealed record SqlitePageEvidence(string Title, long ContentBytes, string LastModified);
public sealed record SqliteRedirectEvidence(string FromName, string ToTarget);
public sealed record SqliteAssetEvidence(string FileName, long DataBytes, string MimeType);
public sealed record SqliteSearchEvidence(string Title, long PlainContentBytes);
