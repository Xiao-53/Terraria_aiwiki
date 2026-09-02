using System.Text.Json;
using System.Security.Cryptography;
using HtmlAgilityPack;
using SQLite;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Compatibility;

public sealed class WikiCompatibilityException : Exception
{
    public WikiCompatibilityException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

public sealed record LegacyDatabaseNormalizationResult(
    string DatabasePath,
    bool Changed,
    IReadOnlyList<string> PreservedSidecars);

public sealed record LegacyDataDirectoryMigrationResult(
    string DataDirectory,
    bool DirectoryChanged,
    LegacyDatabaseNormalizationResult Database);

public sealed record LegacyAnchorMigrationResult(bool Required, int PagesProcessed, int PagesChanged);

public static class LegacyWikiCompatibility
{
    public const string LegacyDataFolder = "Terraria_Wiki";
    public const string LegacyDatabaseName = "Terraria_Wiki.db";
    public const string CurrentDatabaseName = "data.db";

    private static readonly Version AnchorMigrationVersion = new(0, 4);
    private static readonly JsonSerializerOptions JournalJsonOptions = new(JsonSerializerDefaults.General);

    public static bool RequiresLegacyPackageConfirmation(string? appVersion) =>
        appVersion is not null
        && Version.TryParse(appVersion, out var parsed)
        && parsed < AnchorMigrationVersion;

    public static LegacyDatabaseNormalizationResult NormalizeDatabaseFamily(string dataDirectory)
    {
        var directory = ValidateExistingDirectory(dataDirectory);
        var legacyDatabase = Path.Combine(directory, LegacyDatabaseName);
        var currentDatabase = Path.Combine(directory, CurrentDatabaseName);
        var legacyWal = legacyDatabase + "-wal";
        var legacyShm = legacyDatabase + "-shm";
        var currentWal = currentDatabase + "-wal";
        var currentShm = currentDatabase + "-shm";
        var journalPath = Path.Combine(directory, ".wiki-database-family-normalization.json");
        if (File.Exists(journalPath))
            return CompleteDatabaseFamilyNormalization(
                ReadAndValidateDatabaseFamilyJournal(journalPath, directory));

        var hasLegacyDatabase = File.Exists(legacyDatabase);
        var hasCurrentDatabase = File.Exists(currentDatabase);
        var hasLegacyWal = File.Exists(legacyWal);
        var hasLegacyShm = File.Exists(legacyShm);
        var hasCurrentWal = File.Exists(currentWal);
        var hasCurrentShm = File.Exists(currentShm);

        if (hasLegacyDatabase && hasCurrentDatabase)
            throw Error("database-family-conflict", "Both Terraria_Wiki.db and data.db exist.");
        if (!hasLegacyDatabase && !hasCurrentDatabase)
        {
            if (hasLegacyWal || hasLegacyShm || hasCurrentWal || hasCurrentShm)
                throw Error("orphan-database-sidecar", "A WAL/SHM sidecar exists without a supported content database.");
            throw Error("content-database-not-found", "The data directory does not contain a supported content database.");
        }
        if (hasLegacyWal && hasCurrentWal || hasLegacyShm && hasCurrentShm)
            throw Error("database-family-conflict", "Legacy and current database sidecars conflict.");

        if (hasCurrentDatabase)
        {
            if (hasLegacyWal || hasLegacyShm)
                throw Error("orphan-database-sidecar", "Legacy sidecars cannot be associated with data.db without a recovery journal.");
            return new LegacyDatabaseNormalizationResult(
                currentDatabase,
                Changed: false,
                ExistingSidecars(currentWal, currentShm));
        }

        if (hasCurrentWal || hasCurrentShm)
            throw Error("orphan-database-sidecar", "Current sidecars cannot be associated with Terraria_Wiki.db without a recovery journal.");

        var moves = new List<DatabaseFamilyMove>();
        if (hasLegacyWal) moves.Add(CreateDatabaseFamilyMove("wal", legacyWal, currentWal, includeHash: true));
        if (hasLegacyShm) moves.Add(CreateDatabaseFamilyMove("shm", legacyShm, currentShm, includeHash: true));
        moves.Add(CreateDatabaseFamilyMove("database", legacyDatabase, currentDatabase, includeHash: false));
        var journal = new DatabaseFamilyJournal(
            Version: 1,
            NormalizationId: Guid.NewGuid().ToString("N"),
            JournalPath: journalPath,
            DataDirectory: directory,
            CompletedMoves: 0,
            Moves: moves);
        WriteDatabaseFamilyJournal(journal);
        return CompleteDatabaseFamilyNormalization(journal);
    }

    public static LegacyDataDirectoryMigrationResult MigrateLegacyDataDirectory(
        string dataRoot,
        string dataFolder)
    {
        var root = ValidateRoot(dataRoot);
        var folder = ValidateDataFolder(dataFolder);
        var oldDirectory = Path.Combine(root, LegacyDataFolder);
        var newDirectory = Path.Combine(root, folder);

        if (string.Equals(oldDirectory, newDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var normalized = NormalizeDatabaseFamily(newDirectory);
            return new LegacyDataDirectoryMigrationResult(newDirectory, false, normalized);
        }

        var oldExists = Directory.Exists(oldDirectory);
        var newExists = Directory.Exists(newDirectory);
        if (oldExists && newExists)
            throw Error("data-directory-conflict", "Both the legacy and target Wiki data directories exist.");
        if (!oldExists)
        {
            if (!newExists)
                throw Error("data-directory-not-found", "Neither the legacy nor target Wiki data directory exists.");
            var normalized = NormalizeDatabaseFamily(newDirectory);
            return new LegacyDataDirectoryMigrationResult(newDirectory, false, normalized);
        }

        Directory.Move(oldDirectory, newDirectory);
        try
        {
            var normalized = NormalizeDatabaseFamily(newDirectory);
            return new LegacyDataDirectoryMigrationResult(newDirectory, true, normalized);
        }
        catch (Exception migrationException)
        {
            try
            {
                if (Directory.Exists(newDirectory) && !Directory.Exists(oldDirectory))
                    Directory.Move(newDirectory, oldDirectory);
            }
            catch (Exception rollbackException)
            {
                throw Error(
                    "data-directory-rollback-failed",
                    "Legacy data-directory migration failed and the directory rename could not be rolled back.",
                    new AggregateException(migrationException, rollbackException));
            }

            throw Error("data-directory-migration-failed", "Legacy data-directory migration failed and was rolled back.", migrationException);
        }
    }

    public static Task<LegacyAnchorMigrationResult> MigrateAnchorDataAsync(
        string databasePath,
        string defaultPageTitle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath))
            throw new ArgumentException("An absolute database path is required.", nameof(databasePath));
        if (string.IsNullOrWhiteSpace(defaultPageTitle))
            throw new ArgumentException("A default page title is required.", nameof(defaultPageTitle));

        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
            throw Error("content-database-not-found", $"Content database does not exist: {fullPath}.");

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = new SQLiteConnection(
                fullPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            if (!RequiresAnchorMigration(connection, defaultPageTitle))
                return new LegacyAnchorMigrationResult(false, 0, 0);

            var processed = 0;
            var changed = 0;
            connection.RunInTransaction(() =>
            {
                var titles = connection.Query<PageTitleRow>("SELECT Title FROM WikiPage ORDER BY Title");
                foreach (var title in titles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = connection.Find<WikiPage>(title.Title);
                    if (page is null) continue;

                    var document = new HtmlDocument();
                    document.LoadHtml(page.Content);
                    var anchors = document.DocumentNode.SelectNodes("//a[@title]");
                    var pageChanged = false;
                    if (anchors is not null)
                    {
                        foreach (var anchor in anchors)
                        {
                            var titleValue = anchor.GetAttributeValue("title", string.Empty);
                            if (string.IsNullOrEmpty(titleValue)) continue;
                            anchor.SetAttributeValue("data-wiki", titleValue);
                            pageChanged = true;
                        }
                    }

                    if (pageChanged)
                    {
                        page.Content = document.DocumentNode.OuterHtml;
                        connection.Update(page);
                        changed++;
                    }
                    processed++;
                }
            });

            return new LegacyAnchorMigrationResult(true, processed, changed);
        }, cancellationToken);
    }

    public static Task<LegacyAnchorMigrationResult> MigrateAnchorDataSafelyAsync(
        string databasePath,
        string defaultPageTitle,
        CancellationToken cancellationToken = default) =>
        MigrateAnchorDataSafelyCoreAsync(
            databasePath,
            defaultPageTitle,
            static (source, destination) => File.Copy(source, destination, overwrite: false),
            cancellationToken);

    internal static async Task<LegacyAnchorMigrationResult> MigrateAnchorDataSafelyCoreAsync(
        string databasePath,
        string defaultPageTitle,
        Action<string, string> copyDatabase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copyDatabase);
        var target = ValidateDatabasePath(databasePath);
        RecoverPendingAnchorMigration(target);
        RejectDatabaseSidecars(target);

        if (!await RequiresAnchorMigrationAsync(target, defaultPageTitle, cancellationToken))
            return new LegacyAnchorMigrationResult(false, 0, 0);

        var migrationId = Guid.NewGuid().ToString("N");
        var staging = target + $".anchor-staging-{migrationId}";
        var backup = target + $".anchor-backup-{migrationId}";
        var journalPath = target + ".anchor-migration.json";
        AnchorMigrationJournal? journal = null;
        try
        {
            copyDatabase(target, staging);
            var result = await MigrateAnchorDataAsync(staging, defaultPageTitle, cancellationToken);
            RejectDatabaseSidecars(staging);
            ValidateMigratedDatabase(staging);
            if (!result.Required)
            {
                DeleteDatabaseFamilyFiles(staging);
                return result;
            }

            journal = new AnchorMigrationJournal(
                journalPath,
                target,
                staging,
                backup,
                migrationId,
                AnchorMigrationPhase.Prepared);
            WriteAnchorJournal(journal);
            File.Move(target, backup);
            journal = UpdateAnchorJournal(journal, AnchorMigrationPhase.TargetBackedUp);
            File.Move(staging, target);
            journal = UpdateAnchorJournal(journal, AnchorMigrationPhase.TargetInstalled);
            CompleteAnchorMigration(journal);
            return result;
        }
        catch
        {
            if (journal is not null)
                RecoverPendingAnchorMigration(target);
            else
                DeleteDatabaseFamilyFiles(staging);
            throw;
        }
    }

    private static Task<bool> RequiresAnchorMigrationAsync(
        string databasePath,
        string defaultPageTitle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(defaultPageTitle))
            throw new ArgumentException("A default page title is required.", nameof(defaultPageTitle));

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = new SQLiteConnection(
                databasePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            return RequiresAnchorMigration(connection, defaultPageTitle);
        }, cancellationToken);
    }

    private static bool RequiresAnchorMigration(SQLiteConnection connection, string defaultPageTitle)
    {
        var defaultPage = connection.Find<WikiPage>(defaultPageTitle);
        return defaultPage is not null
            && defaultPage.Content.Contains("<a ", StringComparison.OrdinalIgnoreCase)
            && !defaultPage.Content.Contains("data-wiki", StringComparison.Ordinal);
    }

    internal static Task CheckpointDatabaseForMigrationAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var target = ValidateDatabasePath(databasePath);
        if (!File.Exists(target + "-wal") && !File.Exists(target + "-shm"))
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (var connection = new SQLiteConnection(
                    target,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex))
                {
                    var busy = connection.ExecuteScalar<int>("PRAGMA wal_checkpoint(TRUNCATE);");
                    if (busy != 0)
                        throw CheckpointFailed();
                    var journalMode = connection.ExecuteScalar<string>("PRAGMA journal_mode = DELETE;");
                    if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
                        throw CheckpointFailed();
                }

                if (File.Exists(target + "-wal") || File.Exists(target + "-shm"))
                    throw CheckpointFailed();
            }
            catch (WikiCompatibilityException)
            {
                throw;
            }
            catch (Exception exception) when (exception is SQLiteException or IOException or UnauthorizedAccessException)
            {
                throw CheckpointFailed(exception);
            }
        }, cancellationToken);
    }

    public static bool RecoverPendingAnchorMigration(string databasePath)
    {
        var target = ValidateDatabasePath(databasePath, mustExist: false);
        var journalPath = target + ".anchor-migration.json";
        if (!File.Exists(journalPath)) return false;

        var journal = ReadAndValidateAnchorJournal(journalPath, target);
        RejectDatabaseSidecars(journal.TargetPath);
        DeleteOrphanedDatabaseSidecars(journal.StagingPath);
        DeleteOrphanedDatabaseSidecars(journal.BackupPath);
        RejectDatabaseSidecars(journal.StagingPath);
        RejectDatabaseSidecars(journal.BackupPath);
        var targetExists = File.Exists(target);
        var stagingExists = File.Exists(journal.StagingPath);
        var backupExists = File.Exists(journal.BackupPath);
        var committed = targetExists
            && (journal.Phase >= AnchorMigrationPhase.TargetInstalled
                || !stagingExists && !backupExists);
        if (committed)
        {
            CompleteAnchorMigration(journal);
            return true;
        }

        var quarantine = target + $".anchor-failed-{Guid.NewGuid():N}";
        if (targetExists && backupExists && !stagingExists)
            File.Move(target, quarantine);
        if (backupExists && !File.Exists(target))
            File.Move(journal.BackupPath, target);
        if (!File.Exists(target))
            throw Error("anchor-migration-rollback-failed", "The original database could not be restored.");

        DeleteDatabaseFamilyFiles(quarantine);
        DeleteDatabaseFamilyFiles(journal.StagingPath);
        DeleteDatabaseFamilyFiles(journal.BackupPath);
        if (!DatabaseFamilyExists(journal.StagingPath)
            && !DatabaseFamilyExists(journal.BackupPath))
            TryDeleteFile(journal.JournalPath);
        return true;
    }

    private static DatabaseFamilyMove CreateDatabaseFamilyMove(
        string role,
        string source,
        string destination,
        bool includeHash)
    {
        var info = new FileInfo(source);
        return new DatabaseFamilyMove(
            role,
            source,
            destination,
            info.Length,
            includeHash ? ComputeSha256(source) : null);
    }

    private static LegacyDatabaseNormalizationResult CompleteDatabaseFamilyNormalization(
        DatabaseFamilyJournal journal)
    {
        ValidateDatabaseFamilyMovePlan(journal);
        var actualCompleted = GetActualCompletedMoves(journal);
        if (actualCompleted < journal.CompletedMoves || actualCompleted > journal.CompletedMoves + 1)
            throw Error(
                "database-family-journal-invalid",
                "The database-family files do not match the recorded recovery phase.");
        if (actualCompleted != journal.CompletedMoves)
            journal = UpdateDatabaseFamilyJournal(journal, actualCompleted);

        try
        {
            for (var index = actualCompleted; index < journal.Moves.Count; index++)
            {
                var move = journal.Moves[index];
                File.Move(move.SourcePath, move.DestinationPath);
                journal = UpdateDatabaseFamilyJournal(journal, index + 1);
            }
        }
        catch (Exception exception)
        {
            try
            {
                RollBackDatabaseFamilyMoves(journal);
                TryDeleteFile(journal.JournalPath);
                TryDeleteFile(journal.JournalPath + ".new");
            }
            catch (Exception rollbackException)
            {
                throw Error(
                    "database-family-rollback-failed",
                    "Database-family normalization failed and its file renames could not be rolled back.",
                    new AggregateException(exception, rollbackException));
            }

            throw Error(
                "database-family-normalization-failed",
                "Database-family normalization failed and was rolled back.",
                exception);
        }

        TryDeleteFile(journal.JournalPath);
        TryDeleteFile(journal.JournalPath + ".new");
        var currentDatabase = Path.Combine(journal.DataDirectory, CurrentDatabaseName);
        return new LegacyDatabaseNormalizationResult(
            currentDatabase,
            Changed: true,
            ExistingSidecars(currentDatabase + "-wal", currentDatabase + "-shm"));
    }

    private static void ValidateDatabaseFamilyMovePlan(DatabaseFamilyJournal journal)
    {
        if (journal.Moves is null || journal.Moves.Count is < 1 or > 3)
            throw Error("database-family-journal-invalid", "The database-family move plan is invalid.");

        var legacyDatabase = Path.Combine(journal.DataDirectory, LegacyDatabaseName);
        var currentDatabase = Path.Combine(journal.DataDirectory, CurrentDatabaseName);
        var expectedPaths = new Dictionary<string, (string Source, string Destination)>(StringComparer.Ordinal)
        {
            ["wal"] = (legacyDatabase + "-wal", currentDatabase + "-wal"),
            ["shm"] = (legacyDatabase + "-shm", currentDatabase + "-shm"),
            ["database"] = (legacyDatabase, currentDatabase),
        };
        var expectedOrder = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["wal"] = 0,
            ["shm"] = 1,
            ["database"] = 2,
        };
        var seenRoles = new HashSet<string>(StringComparer.Ordinal);
        var lastOrder = -1;
        foreach (var move in journal.Moves)
        {
            if (move is null
                || !expectedPaths.TryGetValue(move.Role, out var expected)
                || !seenRoles.Add(move.Role)
                || expectedOrder[move.Role] <= lastOrder
                || string.IsNullOrWhiteSpace(move.SourcePath)
                || string.IsNullOrWhiteSpace(move.DestinationPath)
                || !PathsEqual(move.SourcePath, expected.Source)
                || !PathsEqual(move.DestinationPath, expected.Destination)
                || move.Length < 0)
                throw Error("database-family-journal-invalid", "The database-family move plan is invalid.");

            var isDatabase = string.Equals(move.Role, "database", StringComparison.Ordinal);
            if (isDatabase != string.IsNullOrEmpty(move.Sha256)
                || !isDatabase && (move.Sha256!.Length != 64
                    || move.Sha256.Any(character => !char.IsAsciiHexDigit(character))))
                throw Error("database-family-journal-invalid", "The database-family move identity is invalid.");
            lastOrder = expectedOrder[move.Role];
        }

        if (!seenRoles.Contains("database")
            || journal.Moves[^1].Role != "database"
            || (!seenRoles.Contains("wal")
                && (File.Exists(legacyDatabase + "-wal") || File.Exists(currentDatabase + "-wal")))
            || (!seenRoles.Contains("shm")
                && (File.Exists(legacyDatabase + "-shm") || File.Exists(currentDatabase + "-shm"))))
            throw Error("database-family-journal-invalid", "The database-family move plan omits an existing file.");
    }

    private static int GetActualCompletedMoves(DatabaseFamilyJournal journal)
    {
        var completed = 0;
        var sourceSeen = false;
        foreach (var move in journal.Moves)
        {
            var sourceExists = File.Exists(move.SourcePath);
            var destinationExists = File.Exists(move.DestinationPath);
            if (sourceExists == destinationExists)
                throw Error("database-family-journal-invalid", "A database-family move has an ambiguous file state.");

            var existing = destinationExists ? move.DestinationPath : move.SourcePath;
            var info = new FileInfo(existing);
            if (info.Length != move.Length
                || move.Sha256 is not null
                && !string.Equals(ComputeSha256(existing), move.Sha256, StringComparison.OrdinalIgnoreCase))
                throw Error("database-family-journal-invalid", "A database-family file does not match its recorded identity.");

            if (destinationExists)
            {
                if (sourceSeen)
                    throw Error("database-family-journal-invalid", "Database-family moves are not a completed prefix.");
                completed++;
            }
            else
            {
                sourceSeen = true;
            }
        }
        return completed;
    }

    private static DatabaseFamilyJournal UpdateDatabaseFamilyJournal(
        DatabaseFamilyJournal journal,
        int completedMoves)
    {
        var updated = journal with { CompletedMoves = completedMoves };
        WriteDatabaseFamilyJournal(updated);
        return updated;
    }

    private static void RollBackDatabaseFamilyMoves(DatabaseFamilyJournal journal)
    {
        for (var index = journal.Moves.Count - 1; index >= 0; index--)
        {
            var move = journal.Moves[index];
            if (File.Exists(move.DestinationPath) && !File.Exists(move.SourcePath))
                File.Move(move.DestinationPath, move.SourcePath);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void RejectDatabaseSidecars(string databasePath)
    {
        if (File.Exists(databasePath + "-wal") || File.Exists(databasePath + "-shm"))
            throw Error(
                "database-not-checkpointed",
                "The database must be closed without WAL/SHM sidecars before anchor migration.");
    }

    private static WikiCompatibilityException CheckpointFailed(Exception? innerException = null) => Error(
        "database-checkpoint-failed",
        "Wiki 数据库尚未安全关闭。请关闭其他 Wiki 程序后重试，或重新选择数据文件夹。",
        innerException);

    private static void DeleteDatabaseFamilyFiles(string databasePath)
    {
        TryDeleteFile(databasePath);
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");
    }

    private static void DeleteOrphanedDatabaseSidecars(string databasePath)
    {
        if (File.Exists(databasePath)) return;
        TryDeleteFile(databasePath + "-wal");
        TryDeleteFile(databasePath + "-shm");
        if (File.Exists(databasePath + "-wal") || File.Exists(databasePath + "-shm"))
            throw Error(
                "anchor-migration-cleanup-failed",
                "An orphaned anchor-migration sidecar could not be removed.");
    }

    private static bool DatabaseFamilyExists(string databasePath) =>
        File.Exists(databasePath)
        || File.Exists(databasePath + "-wal")
        || File.Exists(databasePath + "-shm");

    private static void WriteDatabaseFamilyJournal(DatabaseFamilyJournal journal)
    {
        var temporary = journal.JournalPath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal, JournalJsonOptions));
        File.Move(temporary, journal.JournalPath, overwrite: true);
    }

    private static DatabaseFamilyJournal ReadAndValidateDatabaseFamilyJournal(string journalPath, string directory)
    {
        var journal = JsonSerializer.Deserialize<DatabaseFamilyJournal>(
                File.ReadAllText(journalPath),
                JournalJsonOptions)
            ?? throw Error("database-family-journal-invalid", "The database-family recovery journal is empty.");
        if (journal.Version != 1
            || string.IsNullOrWhiteSpace(journal.NormalizationId)
            || journal.NormalizationId.Length != 32
            || journal.NormalizationId.Any(character => !char.IsAsciiHexDigit(character))
            || string.IsNullOrWhiteSpace(journal.JournalPath)
            || string.IsNullOrWhiteSpace(journal.DataDirectory)
            || !PathsEqual(journal.JournalPath, journalPath)
            || !PathsEqual(journal.DataDirectory, directory)
            || journal.Moves is null
            || journal.CompletedMoves < 0
            || journal.CompletedMoves > journal.Moves.Count)
            throw Error("database-family-journal-invalid", "The database-family recovery journal paths are invalid.");
        ValidateDatabaseFamilyMovePlan(journal);
        return journal;
    }

    private static string ValidateDatabasePath(string databasePath, bool mustExist = true)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath))
            throw new ArgumentException("An absolute database path is required.", nameof(databasePath));
        var fullPath = Path.GetFullPath(databasePath);
        if (mustExist && !File.Exists(fullPath))
            throw Error("content-database-not-found", $"Content database does not exist: {fullPath}.");
        return fullPath;
    }

    private static void ValidateMigratedDatabase(string databasePath)
    {
        try
        {
            WikiContentSchemaValidator.Validate(databasePath);
        }
        catch (WikiContentSchemaValidationException exception)
        {
            throw Error(exception.Code, exception.Message, exception.InnerException);
        }
    }

    private static AnchorMigrationJournal UpdateAnchorJournal(
        AnchorMigrationJournal journal,
        AnchorMigrationPhase phase)
    {
        var updated = journal with { Phase = phase };
        WriteAnchorJournal(updated);
        return updated;
    }

    private static void WriteAnchorJournal(AnchorMigrationJournal journal)
    {
        var temporary = journal.JournalPath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal, JournalJsonOptions));
        File.Move(temporary, journal.JournalPath, overwrite: true);
    }

    private static AnchorMigrationJournal ReadAndValidateAnchorJournal(
        string journalPath,
        string target)
    {
        AnchorMigrationJournal journal;
        try
        {
            var json = File.ReadAllText(journalPath);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(AnchorMigrationJournal.Phase), out var phaseElement)
                || phaseElement.ValueKind != JsonValueKind.Number
                || !phaseElement.TryGetInt32(out _))
                throw Error("anchor-migration-journal-invalid", "The anchor migration journal phase is missing or invalid.");
            journal = JsonSerializer.Deserialize<AnchorMigrationJournal>(json, JournalJsonOptions)
                ?? throw Error("anchor-migration-journal-invalid", "The anchor migration journal is empty.");
        }
        catch (WikiCompatibilityException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Error("anchor-migration-journal-invalid", "The anchor migration journal could not be read.", exception);
        }
        if (!Enum.IsDefined(journal.Phase)
            || string.IsNullOrWhiteSpace(journal.MigrationId)
            || journal.MigrationId.Length != 32
            || journal.MigrationId.Any(character => !char.IsAsciiHexDigit(character))
            || string.IsNullOrWhiteSpace(journal.JournalPath)
            || string.IsNullOrWhiteSpace(journal.TargetPath)
            || string.IsNullOrWhiteSpace(journal.StagingPath)
            || string.IsNullOrWhiteSpace(journal.BackupPath)
            || !PathsEqual(journal.JournalPath, journalPath)
            || !PathsEqual(journal.TargetPath, target)
            || !PathsEqual(journal.StagingPath, target + $".anchor-staging-{journal.MigrationId}")
            || !PathsEqual(journal.BackupPath, target + $".anchor-backup-{journal.MigrationId}"))
            throw Error("anchor-migration-journal-invalid", "The anchor migration journal paths are invalid.");
        return journal;
    }

    private static void CompleteAnchorMigration(AnchorMigrationJournal journal)
    {
        if (!File.Exists(journal.TargetPath))
            throw Error("anchor-migration-commit-invalid", "The migrated database is missing.");
        DeleteDatabaseFamilyFiles(journal.StagingPath);
        DeleteDatabaseFamilyFiles(journal.BackupPath);
        if (!DatabaseFamilyExists(journal.StagingPath)
            && !DatabaseFamilyExists(journal.BackupPath))
            TryDeleteFile(journal.JournalPath);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static IReadOnlyList<string> ExistingSidecars(params string[] paths) =>
        paths.Where(File.Exists).ToArray();

    private static string ValidateExistingDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute data directory is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw Error("data-directory-not-found", $"Data directory does not exist: {fullPath}.");
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw Error("data-directory-reparse-point", "A data directory cannot be a reparse point.");
        return fullPath;
    }

    private static string ValidateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute data root is required.", nameof(path));
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(fullPath))
            throw Error("data-root-not-found", $"Data root does not exist: {fullPath}.");
        return fullPath;
    }

    private static string ValidateDataFolder(string dataFolder)
    {
        if (string.IsNullOrWhiteSpace(dataFolder)
            || Path.IsPathFullyQualified(dataFolder)
            || dataFolder is "." or ".."
            || dataFolder.Contains(Path.DirectorySeparatorChar)
            || dataFolder.Contains(Path.AltDirectorySeparatorChar)
            || dataFolder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The data folder must be one safe relative path segment.", nameof(dataFolder));
        return dataFolder;
    }

    private static WikiCompatibilityException Error(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    private sealed class PageTitleRow
    {
        public string Title { get; set; } = string.Empty;
    }

    private enum AnchorMigrationPhase
    {
        Prepared,
        TargetBackedUp,
        TargetInstalled,
    }

    private sealed record DatabaseFamilyJournal(
        int Version,
        string NormalizationId,
        string JournalPath,
        string DataDirectory,
        int CompletedMoves,
        List<DatabaseFamilyMove> Moves);

    private sealed record DatabaseFamilyMove(
        string Role,
        string SourcePath,
        string DestinationPath,
        long Length,
        string? Sha256);

    private sealed record AnchorMigrationJournal(
        string JournalPath,
        string TargetPath,
        string StagingPath,
        string BackupPath,
        string MigrationId,
        AnchorMigrationPhase Phase);
}
