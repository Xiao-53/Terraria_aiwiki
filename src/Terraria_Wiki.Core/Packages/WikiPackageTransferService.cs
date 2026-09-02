using System.Text.Json;
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

    private static bool IsDatabaseFamilyMember(string path, string databasePath) =>
        PathsEqual(path, databasePath)
        || PathsEqual(path, databasePath + "-wal")
        || PathsEqual(path, databasePath + "-shm");

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

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
