using System.Security.Cryptography;
using System.Text.Json;
using SQLite;
using Terraria_Wiki.Core.Compatibility;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Packages;

public sealed record WikiPackageImportRequest(
    string PackagePath,
    string DataRoot,
    string InspectionToken,
    int? MapToWikiId = null,
    bool AllowLegacyPackage = false);

public sealed record WikiPackageImportResult(
    WikiBook WikiBook,
    WikiPackageInspection Inspection,
    bool ReplacedExistingData,
    bool MigratedLegacyAnchors);

/// <summary>
/// Coordinates verified packages with SQLite connections and the Manager database.
/// UI file pickers, Viewer lifecycle, and old-version confirmation remain host concerns.
/// </summary>
public sealed class WikiPackageTransferService
{
    private static readonly JsonSerializerOptions JournalJsonOptions = new(JsonSerializerDefaults.General);

    private readonly ManagerDbService _managerDatabase;
    private readonly ContentDbService _activeContentDatabase;
    private readonly WikiDataOperationGate _operationGate;

    public WikiPackageTransferService(
        ManagerDbService managerDatabase,
        ContentDbService activeContentDatabase,
        WikiDataOperationGate operationGate)
    {
        _managerDatabase = managerDatabase ?? throw new ArgumentNullException(nameof(managerDatabase));
        _activeContentDatabase = activeContentDatabase ?? throw new ArgumentNullException(nameof(activeContentDatabase));
        _operationGate = operationGate ?? throw new ArgumentNullException(nameof(operationGate));
    }

    public Task<WikiPackageInspection> InspectAsync(
        string packagePath,
        WikiPackageOptions? options = null,
        CancellationToken cancellationToken = default) =>
        WikiPackageReader.InspectAsync(packagePath, options, cancellationToken);

    public async Task<WikiDatabaseInspection> InspectDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        var path = ValidateStandaloneDatabasePath(databasePath);
        ValidateDatabase(path);
        var token = await ComputeSha256Async(path, cancellationToken);
        return new WikiDatabaseInspection(path, new FileInfo(path).Length, token);
    }

    public async Task<WikiDatabaseExportResult> ExportDatabaseAsync(
        WikiDataPaths paths,
        string databasePath,
        DatabaseService sourceDatabase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(sourceDatabase);
        var target = ValidateNewDatabaseTarget(databasePath);
        if (PathsEqual(target, paths.ContentDatabasePath)
            || IsPathWithinDirectory(target, paths.DataDirectory))
        {
            throw Error("database-export-target-invalid", "The export target must be outside the managed Wiki data directory.");
        }

        using var lease = await EnterAsync(cancellationToken);
        try
        {
            if (!File.Exists(paths.ContentDatabasePath))
                throw Error("content-database-not-found", $"Content database does not exist: {paths.ContentDatabasePath}.");
            if (!PathsEqual(sourceDatabase.DatabasePath, paths.ContentDatabasePath))
                throw Error("database-source-mismatch", "The snapshot source does not match the selected Wiki data directory.");

            await sourceDatabase.BackupExistingAsync(target, cancellationToken);
            await FinalizeStandaloneDatabaseAsync(target, cancellationToken);
            ValidateDatabase(target);
            return new WikiDatabaseExportResult(target, new FileInfo(target).Length);
        }
        catch
        {
            TryDeleteFile(target);
            throw;
        }
    }

    public async Task<WikiPackageWriteResult> ExportAsync(
        WikiBook book,
        WikiDataPaths paths,
        string packagePath,
        string appVersion,
        DatabaseService sourceDatabase,
        WikiPackageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(sourceDatabase);
        using var lease = await EnterAsync(cancellationToken);
        string? staging = null;
        try
        {
            staging = paths.CreateUniqueStagingDirectory("export");
            if (!File.Exists(paths.ContentDatabasePath))
                throw Error("content-database-not-found", $"Content database does not exist: {paths.ContentDatabasePath}.");
            if (!PathsEqual(sourceDatabase.DatabasePath, paths.ContentDatabasePath))
                throw Error("database-source-mismatch", "The snapshot source does not match the selected Wiki data directory.");

            var databaseSnapshot = Path.Combine(staging, "data.db");
            await sourceDatabase.BackupExistingAsync(databaseSnapshot, cancellationToken);
            var sources = new List<WikiPackageSourceFile>
            {
                new("data.db", databaseSnapshot)
            };

            var enumeration = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var sourcePath in Directory.EnumerateFiles(paths.DataDirectory, "*", enumeration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsDatabaseFamilyMember(sourcePath, paths.ContentDatabasePath)) continue;
                var relativePath = Path.GetRelativePath(paths.DataDirectory, sourcePath).Replace('\\', '/');
                sources.Add(new WikiPackageSourceFile(relativePath, sourcePath));
            }

            return await WikiPackageWriter.WriteAsync(
                packagePath,
                new WikiPackageDescriptor(
                    book.Id,
                    book.Title,
                    book.IsPageDownloaded,
                    book.IsResourceDownloaded,
                    book.UpdateTime,
                    appVersion),
                sources,
                options,
                cancellationToken);
        }
        finally
        {
            if (staging is not null) TryDeleteDirectory(staging);
        }
    }

    public async Task<WikiPackageImportResult> ImportAsync(
        WikiPackageImportRequest request,
        WikiPackageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var lease = await EnterAsync(cancellationToken);
        ImportJournal? journal = null;
        WikiBook? previousBook = null;
        var activeTarget = false;
        try
        {
            var inspection = await WikiPackageReader.InspectAsync(request.PackagePath, options, cancellationToken);
            if (!string.Equals(inspection.InspectionToken, request.InspectionToken, StringComparison.Ordinal))
                throw Error("inspection-token-mismatch", "The supplied inspection token does not match the package.");

            var targetId = request.MapToWikiId ?? inspection.Metadata.Id;
            var book = await _managerDatabase.GetItemAsync<WikiBook>(targetId, cancellationToken)
                ?? throw Error("unknown-wiki", $"WikiBook {targetId} is not registered in Manager.db.");
            if (targetId != inspection.Metadata.Id && request.MapToWikiId is null)
                throw Error("unknown-wiki", $"Package WikiBook {inspection.Metadata.Id} requires an explicit mapping.");
            if (LegacyWikiCompatibility.RequiresLegacyPackageConfirmation(inspection.Metadata.AppVersion)
                && !request.AllowLegacyPackage)
                throw Error("legacy-confirmation-required", "This package predates 0.4 and requires explicit confirmation.");

            previousBook = CloneBook(book);
            var paths = WikiDataPaths.Create(request.DataRoot, book);
            var staging = paths.CreateUniqueStagingDirectory("import");
            var importId = Path.GetFileName(staging)["import-".Length..];
            var backup = paths.DataDirectory + $".import-backup-{importId}";
            var journalPath = Path.Combine(paths.StagingRoot, $"import-{book.Id}.journal.json");
            journal = new ImportJournal(
                journalPath,
                paths.RootPath,
                book.Id,
                importId,
                paths.DataDirectory,
                staging,
                backup,
                ImportPhase.Created,
                CloneBook(book));
            WriteJournal(journal);

            await WikiPackageReader.ExtractAsync(
                request.PackagePath,
                staging,
                inspection.InspectionToken,
                options,
                cancellationToken);
            var normalized = LegacyWikiCompatibility.NormalizeDatabaseFamily(staging);
            ValidateDatabase(normalized.DatabasePath);

            var migrateAnchors = LegacyWikiCompatibility.RequiresLegacyPackageConfirmation(inspection.Metadata.AppVersion);
            var anchorMigration = migrateAnchors
                ? await LegacyWikiCompatibility.MigrateAnchorDataAsync(
                    normalized.DatabasePath,
                    book.DefaultPageTitle,
                    cancellationToken)
                : new LegacyAnchorMigrationResult(false, 0, 0);

            journal = UpdateJournal(journal, ImportPhase.Validated);
            activeTarget = PathsEqual(_activeContentDatabase.DatabasePath, paths.ContentDatabasePath);
            if (activeTarget) await _activeContentDatabase.CloseConnection(cancellationToken);

            var replaced = Directory.Exists(paths.DataDirectory);
            if (replaced)
            {
                Directory.Move(paths.DataDirectory, backup);
                journal = UpdateJournal(journal, ImportPhase.TargetBackedUp);
            }

            Directory.Move(staging, paths.DataDirectory);
            journal = UpdateJournal(journal, ImportPhase.TargetInstalled);

            book.IsPageDownloaded = inspection.Metadata.IsPageDownloaded;
            book.IsResourceDownloaded = inspection.Metadata.IsResourceDownloaded;
            book.UpdateTime = inspection.Metadata.UpdateTime;
            await _managerDatabase.SaveItemAsync(book, cancellationToken);
            journal = UpdateJournal(journal, ImportPhase.ManagerUpdated);

            if (activeTarget)
            {
                await _activeContentDatabase.SwitchDatabaseAsync(paths.ContentDatabasePath, cancellationToken);
                await _activeContentDatabase.Init(seedBook: book, cancellationToken: cancellationToken);
            }

            journal = UpdateJournal(journal, ImportPhase.Committed);
            CompleteCommittedImport(journal);
            return new WikiPackageImportResult(book, inspection, replaced, anchorMigration.Required);
        }
        catch (Exception importException)
        {
            if (journal is not null)
            {
                try
                {
                    await RollBackAsync(journal, previousBook, activeTarget, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    throw Error(
                        "import-rollback-failed",
                        "Package import failed and rollback did not complete.",
                        new AggregateException(importException, rollbackException));
                }
            }
            throw;
        }
    }

    public async Task<WikiDatabaseImportResult> ImportDatabaseAsync(
        WikiDatabaseImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var lease = await EnterAsync(cancellationToken);
        ImportJournal? journal = null;
        WikiBook? previousBook = null;
        var activeTarget = false;
        try
        {
            var sourcePath = ValidateStandaloneDatabasePath(request.DatabasePath);
            var sourceBytes = new FileInfo(sourcePath).Length;

            var book = await _managerDatabase.GetItemAsync<WikiBook>(request.WikiId, cancellationToken)
                ?? throw Error("unknown-wiki", $"WikiBook {request.WikiId} is not registered in Manager.db.");
            previousBook = CloneBook(book);
            var paths = WikiDataPaths.Create(request.DataRoot, book);
            if (!PathsEqual(_activeContentDatabase.DatabasePath, paths.ContentDatabasePath))
                throw Error("database-target-not-active", "Raw database import is limited to the active Wiki.");
            if (PathsEqual(sourcePath, paths.ContentDatabasePath)
                || IsPathWithinDirectory(sourcePath, paths.DataDirectory))
            {
                throw Error("database-import-source-invalid", "The import source must be outside the managed Wiki data directory.");
            }

            EnsureImportSpace(paths, sourceBytes);
            var staging = paths.CreateUniqueStagingDirectory("import");
            var importId = Path.GetFileName(staging)["import-".Length..];
            var backup = paths.DataDirectory + $".import-backup-{importId}";
            var journalPath = Path.Combine(paths.StagingRoot, $"import-{book.Id}.journal.json");
            journal = new ImportJournal(
                journalPath,
                paths.RootPath,
                book.Id,
                importId,
                paths.DataDirectory,
                staging,
                backup,
                ImportPhase.Created,
                CloneBook(book));
            WriteJournal(journal);

            CopyAuxiliaryFiles(paths, staging, cancellationToken);
            var stagedDatabase = Path.Combine(staging, LegacyWikiCompatibility.CurrentDatabaseName);
            var copiedToken = await CopyAndHashAsync(sourcePath, stagedDatabase, cancellationToken);
            if (!string.Equals(copiedToken, request.InspectionToken, StringComparison.Ordinal))
                throw Error("inspection-token-mismatch", "The database file changed after it was inspected.");
            var inspection = new WikiDatabaseInspection(sourcePath, sourceBytes, copiedToken);
            ValidateDatabase(stagedDatabase);
            var anchorMigration = await LegacyWikiCompatibility.MigrateAnchorDataAsync(
                stagedDatabase,
                book.DefaultPageTitle,
                cancellationToken);

            journal = UpdateJournal(journal, ImportPhase.Validated);
            activeTarget = true;
            await _activeContentDatabase.CloseConnection(cancellationToken);

            var replaced = Directory.Exists(paths.DataDirectory);
            if (replaced)
            {
                Directory.Move(paths.DataDirectory, backup);
                journal = UpdateJournal(journal, ImportPhase.TargetBackedUp);
            }

            Directory.Move(staging, paths.DataDirectory);
            journal = UpdateJournal(journal, ImportPhase.TargetInstalled);

            await _activeContentDatabase.SwitchDatabaseAsync(paths.ContentDatabasePath, cancellationToken);
            await _activeContentDatabase.Init(seedBook: book, cancellationToken: cancellationToken);
            book.PageCount = await _activeContentDatabase.GetCountAsync<WikiPage>(cancellationToken);
            book.RedirectCount = await _activeContentDatabase.GetCountAsync<WikiRedirect>(cancellationToken);
            book.ResourceCount = await _activeContentDatabase.GetCountAsync<WikiAsset>(cancellationToken);
            book.DataSize = new FileInfo(paths.ContentDatabasePath).Length;
            book.IsPageDownloaded = book.PageCount > 1;
            book.IsResourceDownloaded = book.ResourceCount > 0;
            await _managerDatabase.SaveItemAsync(book, cancellationToken);
            journal = UpdateJournal(journal, ImportPhase.ManagerUpdated);

            journal = UpdateJournal(journal, ImportPhase.Committed);
            CompleteCommittedImport(journal);
            return new WikiDatabaseImportResult(book, inspection, replaced, anchorMigration.Required);
        }
        catch (Exception importException)
        {
            if (journal is not null)
            {
                try
                {
                    await RollBackAsync(journal, previousBook, activeTarget, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    throw Error(
                        "import-rollback-failed",
                        "Database import failed and rollback did not complete.",
                        new AggregateException(importException, rollbackException));
                }
            }
            throw;
        }
    }

    public async Task RecoverPendingImportAsync(
        string dataRoot,
        WikiBook book,
        CancellationToken cancellationToken = default)
    {
        var paths = WikiDataPaths.Create(dataRoot, book);
        var journalPath = Path.Combine(paths.StagingRoot, $"import-{book.Id}.journal.json");
        if (!File.Exists(journalPath)) return;

        using var lease = await EnterAsync(cancellationToken);
        var journal = ReadAndValidateJournal(journalPath, paths, book.Id);
        var activeTarget = PathsEqual(_activeContentDatabase.DatabasePath, paths.ContentDatabasePath);
        if (journal.Phase == ImportPhase.Committed)
            CompleteCommittedImport(journal);
        else
            await RollBackAsync(journal, journal.PreviousBook, activeTarget, cancellationToken);
    }

    internal static bool HasPendingImportJournal(string dataRoot, int wikiId)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot))
            throw new ArgumentException("An absolute data root is required.", nameof(dataRoot));
        if (wikiId <= 0)
            throw new ArgumentOutOfRangeException(nameof(wikiId));
        return File.Exists(Path.Combine(
            Path.GetFullPath(dataRoot),
            "Temp",
            $"import-{wikiId}.journal.json"));
    }

    private async Task RollBackAsync(
        ImportJournal journal,
        WikiBook? previousBook,
        bool activeTarget,
        CancellationToken cancellationToken)
    {
        if (activeTarget) await _activeContentDatabase.CloseConnection(cancellationToken);

        var targetExists = Directory.Exists(journal.TargetDirectory);
        var backupExists = Directory.Exists(journal.BackupDirectory);
        var stagingExists = Directory.Exists(journal.StagingDirectory);
        var installedTargetExists = targetExists
            && (journal.Phase >= ImportPhase.TargetInstalled
                || backupExists
                || (journal.Phase >= ImportPhase.Validated && !stagingExists));

        var quarantine = journal.TargetDirectory + $".failed-import-{Guid.NewGuid():N}";
        if (installedTargetExists)
            Directory.Move(journal.TargetDirectory, quarantine);
        if (Directory.Exists(journal.BackupDirectory) && !Directory.Exists(journal.TargetDirectory))
            Directory.Move(journal.BackupDirectory, journal.TargetDirectory);

        if (previousBook is not null)
            await _managerDatabase.SaveItemAsync(previousBook, cancellationToken);

        if (activeTarget && Directory.Exists(journal.TargetDirectory))
        {
            var databasePath = Path.Combine(journal.TargetDirectory, LegacyWikiCompatibility.CurrentDatabaseName);
            await _activeContentDatabase.SwitchDatabaseAsync(databasePath, cancellationToken);
            await _activeContentDatabase.Init(seedBook: previousBook, cancellationToken: cancellationToken);
        }

        TryDeleteDirectory(quarantine);
        TryDeleteDirectory(journal.StagingDirectory);
        TryDeleteDirectory(journal.BackupDirectory);
        TryDeleteFile(journal.JournalPath);
    }

    private async Task<WikiDataOperationGate.WikiDataOperationLease> EnterAsync(
        CancellationToken cancellationToken)
    {
        var lease = await _operationGate.TryAcquireAsync(cancellationToken);
        if (lease is null)
            throw Error("package-operation-in-progress", "Another package operation is already running.");
        return lease;
    }

    private static void ValidateDatabase(string databasePath)
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

    private static string ValidateStandaloneDatabasePath(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath))
            throw new ArgumentException("An absolute database path is required.", nameof(databasePath));
        var path = Path.GetFullPath(databasePath);
        if (!File.Exists(path))
            throw Error("content-database-not-found", "The selected database file does not exist.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw Error("database-file-invalid", "A reparse-point database file cannot be imported.");
        if (new[] { "-journal", "-wal", "-shm" }.Any(suffix => File.Exists(path + suffix)))
            throw Error("database-sidecar-present", "The selected database must be a standalone SQLite file without sidecars.");
        return path;
    }

    private static string ValidateNewDatabaseTarget(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath))
            throw new ArgumentException("An absolute database path is required.", nameof(databasePath));
        var path = Path.GetFullPath(databasePath);
        _ = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The database path does not have a parent directory.", nameof(databasePath));
        if (File.Exists(path))
            throw Error("database-export-target-exists", "The export target already exists.");
        return path;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static Task FinalizeStandaloneDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var connection = new SQLiteConnection(
                       databasePath,
                       SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex))
            {
                var journalMode = connection.ExecuteScalar<string>("PRAGMA journal_mode = DELETE;");
                if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
                    throw Error("database-export-finalization-failed", "The exported database could not be finalized as a standalone file.");
            }
            if (new[] { "-journal", "-wal", "-shm" }.Any(suffix => File.Exists(databasePath + suffix)))
                throw Error("database-export-finalization-failed", "The exported database still has SQLite sidecars.");
        }, cancellationToken);

    private static async Task<string> CopyAndHashAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        destination.Flush(flushToDisk: true);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void CopyAuxiliaryFiles(
        WikiDataPaths paths,
        string staging,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(paths.DataDirectory)) return;
        foreach (var sourcePath in new[]
                 {
                     paths.ResourceListPath,
                     paths.TemporaryResourceListPath,
                     paths.PageListPath,
                     paths.FailedPageListPath,
                     paths.TemporaryFailedPageListPath,
                     paths.FailedResourceListPath,
                     paths.TemporaryFailedResourceListPath,
                     paths.UpdatePageListPath,
                     paths.UpdateResourceListPath,
                 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(sourcePath))
            {
                continue;
            }
            var relative = Path.GetRelativePath(paths.DataDirectory, sourcePath);
            var destination = Path.Combine(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourcePath, destination, overwrite: false);
        }
    }

    private static void EnsureImportSpace(WikiDataPaths paths, long databaseBytes)
    {
        var root = Path.GetPathRoot(paths.StagingRoot)
            ?? throw Error("database-import-space-unavailable", "The managed data drive could not be resolved.");
        if (new DriveInfo(root).AvailableFreeSpace < databaseBytes)
            throw Error("database-import-space-unavailable", "There is not enough free space to stage the database import.");
    }

    private static bool IsDatabaseFamilyMember(string path, string databasePath) =>
        PathsEqual(path, databasePath)
        || PathsEqual(path, databasePath + "-wal")
        || PathsEqual(path, databasePath + "-shm");

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathWithinDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static ImportJournal UpdateJournal(ImportJournal journal, ImportPhase phase)
    {
        var updated = journal with { Phase = phase };
        WriteJournal(updated);
        return updated;
    }

    private static void WriteJournal(ImportJournal journal)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(journal.JournalPath)!);
        var temporary = journal.JournalPath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal, JournalJsonOptions));
        File.Move(temporary, journal.JournalPath, overwrite: true);
    }

    private static ImportJournal ReadAndValidateJournal(
        string journalPath,
        WikiDataPaths paths,
        int wikiId)
    {
        ImportJournal journal;
        try
        {
            var json = File.ReadAllText(journalPath);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(nameof(ImportJournal.Phase), out var phaseElement)
                || phaseElement.ValueKind != JsonValueKind.Number
                || !phaseElement.TryGetInt32(out _))
                throw Error("import-journal-invalid", "The import recovery journal phase is missing or invalid.");
            journal = JsonSerializer.Deserialize<ImportJournal>(json, JournalJsonOptions)
                ?? throw Error("import-journal-invalid", "The import recovery journal is empty.");
        }
        catch (WikiPackageException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Error("import-journal-invalid", "The import recovery journal could not be read.", exception);
        }
        if (journal.WikiId != wikiId
            || !Enum.IsDefined(journal.Phase)
            || string.IsNullOrWhiteSpace(journal.ImportId)
            || journal.ImportId.Length != 32
            || journal.ImportId.Any(character => !char.IsAsciiHexDigit(character))
            || string.IsNullOrWhiteSpace(journal.DataRoot)
            || string.IsNullOrWhiteSpace(journal.TargetDirectory)
            || string.IsNullOrWhiteSpace(journal.StagingDirectory)
            || string.IsNullOrWhiteSpace(journal.BackupDirectory)
            || string.IsNullOrWhiteSpace(journal.JournalPath)
            || journal.PreviousBook is null
            || journal.PreviousBook.Id != wikiId
            || string.IsNullOrWhiteSpace(journal.PreviousBook.DataFolder)
            || !string.Equals(journal.PreviousBook.DataFolder, paths.DataFolder, StringComparison.OrdinalIgnoreCase)
            || !PathsEqual(journal.DataRoot, paths.RootPath)
            || !PathsEqual(journal.TargetDirectory, paths.DataDirectory)
            || !PathsEqual(journal.StagingDirectory, Path.Combine(paths.StagingRoot, $"import-{journal.ImportId}"))
            || !PathsEqual(journal.BackupDirectory, paths.DataDirectory + $".import-backup-{journal.ImportId}")
            || !PathsEqual(journal.JournalPath, journalPath))
            throw Error("import-journal-invalid", "The import recovery journal does not match the selected Wiki.");

        foreach (var path in new[]
                 {
                     paths.RootPath,
                     paths.StagingRoot,
                     journal.TargetDirectory,
                     journal.StagingDirectory,
                     journal.BackupDirectory,
                 })
            RejectExistingReparsePoint(path);
        return journal;
    }

    private static void RejectExistingReparsePoint(string path)
    {
        if (Directory.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw Error("import-journal-invalid", "The import recovery journal resolves through a reparse-point directory.");
    }

    private static void CompleteCommittedImport(ImportJournal journal)
    {
        if (!Directory.Exists(journal.TargetDirectory))
            throw Error("import-commit-invalid", "The committed import target is missing.");

        TryDeleteDirectory(journal.StagingDirectory);
        TryDeleteDirectory(journal.BackupDirectory);
        if (!Directory.Exists(journal.StagingDirectory) && !Directory.Exists(journal.BackupDirectory))
            TryDeleteFile(journal.JournalPath);
    }

    private static WikiBook CloneBook(WikiBook source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Description = source.Description,
        IsPageDownloaded = source.IsPageDownloaded,
        IsResourceDownloaded = source.IsResourceDownloaded,
        PageCount = source.PageCount,
        ResourceCount = source.ResourceCount,
        RedirectCount = source.RedirectCount,
        DataSize = source.DataSize,
        UpdateTime = source.UpdateTime,
        ApiBaseUrl = source.ApiBaseUrl,
        BaseUrl = source.BaseUrl,
        RedirectListUrl = source.RedirectListUrl,
        MainNamespace = source.MainNamespace,
        AdditionalNamespaces = source.AdditionalNamespaces,
        JunkXPath = source.JunkXPath,
        DataFolder = source.DataFolder,
        DefaultPageContent = source.DefaultPageContent,
        DefaultPageTitle = source.DefaultPageTitle,
    };

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static WikiPackageException Error(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    private enum ImportPhase
    {
        Created,
        Validated,
        TargetBackedUp,
        TargetInstalled,
        ManagerUpdated,
        Committed,
    }

    private sealed record ImportJournal(
        string JournalPath,
        string DataRoot,
        int WikiId,
        string ImportId,
        string TargetDirectory,
        string StagingDirectory,
        string BackupDirectory,
        ImportPhase Phase,
        WikiBook PreviousBook);

}
