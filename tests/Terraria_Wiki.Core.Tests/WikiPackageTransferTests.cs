using System.Text.Json;
using SQLite;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Tests;

public sealed class WikiPackageTransferTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "terraria-transfer-" + Guid.NewGuid().ToString("N"));
    private ManagerDbService? _manager;
    private ContentDbService? _content;

    [Fact]
    public async Task Export_then_active_import_uses_a_snapshot_and_atomically_replaces_data()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transfer = await CreateTransferAsync(book, paths);
        await _content!.SaveItemAsync(new WikiPage
        {
            Title = "Exported",
            Content = "package version",
            LastModified = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)
        });
        await File.WriteAllTextAsync(paths.ResourceListPath, "https://wiki.test/a.png\n");

        var packagePath = Path.Combine(_root, "fixture.pkg");
        var write = await transfer.ExportAsync(book, paths, packagePath, "1.0.0", _content);
        Assert.Equal("data.db", write.Metadata.Files![0].RelativePath);
        Assert.Contains(write.Metadata.Files, file => file.RelativePath == "res.txt");

        await _content.SaveItemAsync(new WikiPage
        {
            Title = "PostExport",
            Content = "must disappear",
            LastModified = DateTime.UtcNow
        });
        book.IsPageDownloaded = false;
        await _manager!.SaveItemAsync(book);

        var inspection = await transfer.InspectAsync(packagePath);
        var result = await transfer.ImportAsync(new WikiPackageImportRequest(
            packagePath,
            _root,
            inspection.InspectionToken));

        Assert.True(result.ReplacedExistingData);
        Assert.Equal("package version", (await _content.GetItemAsync<WikiPage>("Exported"))?.Content);
        Assert.Null(await _content.GetItemAsync<WikiPage>("PostExport"));
        Assert.True((await _manager.GetItemAsync<WikiBook>(book.Id))?.IsPageDownloaded);
        Assert.Equal("https://wiki.test/a.png\n", await File.ReadAllTextAsync(paths.ResourceListPath));
        Assert.Empty(Directory.EnumerateDirectories(_root, "*.import-backup-*"));
        Assert.Empty(Directory.EnumerateFiles(paths.StagingRoot, "*.journal.json"));
    }

    [Fact]
    public async Task Invalid_package_database_is_rejected_before_the_existing_target_is_touched()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transfer = await CreateTransferAsync(book, paths);
        await _content!.SaveItemAsync(new WikiPage { Title = "Keep", Content = "safe" });
        var sentinel = Path.Combine(paths.DataDirectory, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "untouched");

        var invalidDatabase = Path.Combine(_root, "invalid.db");
        await File.WriteAllBytesAsync(invalidDatabase, [1, 2, 3, 4]);
        var packagePath = Path.Combine(_root, "invalid.pkg");
        await WikiPackageWriter.WriteAsync(
            packagePath,
            new WikiPackageDescriptor(book.Id, book.Title, true, false, DateTime.UtcNow, "1.0.0"),
            [new WikiPackageSourceFile("data.db", invalidDatabase)]);
        var inspection = await transfer.InspectAsync(packagePath);

        var error = await Assert.ThrowsAsync<WikiPackageException>(() => transfer.ImportAsync(
            new WikiPackageImportRequest(packagePath, _root, inspection.InspectionToken)));

        Assert.Equal("database-validation-failed", error.Code);
        Assert.Equal("safe", (await _content.GetItemAsync<WikiPage>("Keep"))?.Content);
        Assert.Equal("untouched", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(Directory.EnumerateDirectories(_root, "*.import-backup-*"));
    }

    [Fact]
    public async Task Unknown_package_identity_requires_an_explicit_mapping()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transfer = await CreateTransferAsync(book, paths);

        // Produce an otherwise valid package for an unregistered identity.
        var snapshot = Path.Combine(_root, "unknown-data.db");
        await _content!.BackupExistingAsync(snapshot);
        var unknownPackage = Path.Combine(_root, "unknown-id.pkg");
        await WikiPackageWriter.WriteAsync(
            unknownPackage,
            new WikiPackageDescriptor(404, "Unknown", true, false, DateTime.UtcNow, "1.0.0"),
            [new WikiPackageSourceFile("data.db", snapshot)]);
        var inspection = await transfer.InspectAsync(unknownPackage);

        var error = await Assert.ThrowsAsync<WikiPackageException>(() => transfer.ImportAsync(
            new WikiPackageImportRequest(unknownPackage, _root, inspection.InspectionToken)));

        Assert.Equal("unknown-wiki", error.Code);
    }

    [Fact]
    public async Task Recovery_restores_backup_when_target_move_completed_before_journal_update()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        await CreateTransferAsync(book, paths);
        var originalMarker = Path.Combine(paths.DataDirectory, "original.txt");
        await File.WriteAllTextAsync(originalMarker, "original");
        await _content!.CloseConnection();

        const string importId = "0123456789abcdef0123456789abcdef";
        var staging = Path.Combine(paths.StagingRoot, $"import-{importId}");
        var backup = paths.DataDirectory + $".import-backup-{importId}";
        Directory.Move(paths.DataDirectory, backup);
        Directory.CreateDirectory(paths.DataDirectory);
        await File.WriteAllTextAsync(Path.Combine(paths.DataDirectory, "imported.txt"), "imported");
        var journal = WriteJournal(paths, book, importId, staging, backup, phase: 2);

        var placeholder = new ContentDbService(Path.Combine(_root, "placeholder.db"));
        _content = placeholder;
        var transfer = new WikiPackageTransferService(_manager!, placeholder, new WikiDataOperationGate());
        await transfer.RecoverPendingImportAsync(_root, book);

        Assert.Equal("original", await File.ReadAllTextAsync(originalMarker));
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, "imported.txt")));
        Assert.False(Directory.Exists(backup));
        Assert.False(File.Exists(journal));
    }

    [Fact]
    public async Task Recovery_finishes_a_committed_import_without_restoring_the_backup()
    {
        var book = CreateBook();
        var previousBook = CloneBook(book);
        var paths = WikiDataPaths.Create(_root, book);
        await CreateTransferAsync(book, paths);
        await File.WriteAllTextAsync(Path.Combine(paths.DataDirectory, "original.txt"), "original");
        await _content!.CloseConnection();

        const string importId = "fedcba9876543210fedcba9876543210";
        var staging = Path.Combine(paths.StagingRoot, $"import-{importId}");
        var backup = paths.DataDirectory + $".import-backup-{importId}";
        Directory.Move(paths.DataDirectory, backup);
        Directory.CreateDirectory(paths.DataDirectory);
        await File.WriteAllTextAsync(Path.Combine(paths.DataDirectory, "imported.txt"), "imported");
        book.IsPageDownloaded = false;
        await _manager!.SaveItemAsync(book);
        var journal = WriteJournal(paths, previousBook, importId, staging, backup, phase: 5);

        var placeholder = new ContentDbService(Path.Combine(_root, "placeholder.db"));
        _content = placeholder;
        var transfer = new WikiPackageTransferService(_manager, placeholder, new WikiDataOperationGate());
        await transfer.RecoverPendingImportAsync(_root, book);

        Assert.Equal("imported", await File.ReadAllTextAsync(Path.Combine(paths.DataDirectory, "imported.txt")));
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, "original.txt")));
        Assert.False(Directory.Exists(backup));
        Assert.False(File.Exists(journal));
        Assert.False((await _manager.GetItemAsync<WikiBook>(book.Id))!.IsPageDownloaded);
    }

    [Fact]
    public async Task Import_rejects_an_incomplete_content_schema_before_target_swap()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transfer = await CreateTransferAsync(book, paths);
        var sentinel = Path.Combine(paths.DataDirectory, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "untouched");

        var incompleteDatabase = Path.Combine(_root, "incomplete.db");
        using (var connection = new SQLiteConnection(incompleteDatabase))
            connection.CreateTable<WikiPage>();
        var packagePath = Path.Combine(_root, "incomplete.pkg");
        await WikiPackageWriter.WriteAsync(
            packagePath,
            new WikiPackageDescriptor(book.Id, book.Title, true, false, DateTime.UtcNow, "1.0.0"),
            [new WikiPackageSourceFile("data.db", incompleteDatabase)]);
        var inspection = await transfer.InspectAsync(packagePath);

        var error = await Assert.ThrowsAsync<WikiPackageException>(() => transfer.ImportAsync(
            new WikiPackageImportRequest(packagePath, _root, inspection.InspectionToken)));

        Assert.Equal("database-schema-invalid", error.Code);
        Assert.Equal("untouched", await File.ReadAllTextAsync(sentinel));
    }

    [Fact]
    public async Task Import_rejects_an_ordinary_table_disguised_as_the_fts_index()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transfer = await CreateTransferAsync(book, paths);
        var sentinel = Path.Combine(paths.DataDirectory, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "untouched");

        var counterfeitDatabase = Path.Combine(_root, "counterfeit-fts.db");
        var counterfeit = new ContentDbService(counterfeitDatabase);
        await counterfeit.Init(seedBook: book);
        await counterfeit.CloseConnection();
        using (var connection = new SQLiteConnection(counterfeitDatabase))
        {
            connection.Execute("DROP TABLE WikiSearchIndex;");
            connection.Execute("""
                CREATE TABLE WikiSearchIndex (
                    Title TEXT,
                    PlainContent TEXT /* fts5 trigram */
                );
                """);
        }

        var packagePath = Path.Combine(_root, "counterfeit-fts.pkg");
        await WikiPackageWriter.WriteAsync(
            packagePath,
            new WikiPackageDescriptor(book.Id, book.Title, true, false, DateTime.UtcNow, "1.0.0"),
            [new WikiPackageSourceFile("data.db", counterfeitDatabase)]);
        var inspection = await transfer.InspectAsync(packagePath);

        var error = await Assert.ThrowsAsync<WikiPackageException>(() => transfer.ImportAsync(
            new WikiPackageImportRequest(packagePath, _root, inspection.InspectionToken)));

        Assert.Equal("database-schema-invalid", error.Code);
        Assert.Equal("untouched", await File.ReadAllTextAsync(sentinel));
    }

    [Fact]
    public async Task Invalid_recovery_phase_is_rejected_without_touching_target_or_artifacts()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transfer = await CreateTransferAsync(book, paths);
        var sentinel = Path.Combine(paths.DataDirectory, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "untouched");

        const string importId = "00112233445566778899aabbccddeeff";
        var staging = Path.Combine(paths.StagingRoot, $"import-{importId}");
        var backup = paths.DataDirectory + $".import-backup-{importId}";
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(backup);
        var journal = WriteJournal(paths, book, importId, staging, backup, phase: 999);

        var error = await Assert.ThrowsAsync<WikiPackageException>(
            () => transfer.RecoverPendingImportAsync(_root, book));

        Assert.Equal("import-journal-invalid", error.Code);
        Assert.Equal("untouched", await File.ReadAllTextAsync(sentinel));
        Assert.True(Directory.Exists(staging));
        Assert.True(Directory.Exists(backup));
        Assert.True(File.Exists(journal));
    }

    [Fact]
    public async Task Recovery_rejects_a_journal_for_a_different_previous_book()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transfer = await CreateTransferAsync(book, paths);
        var sentinel = Path.Combine(paths.DataDirectory, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "untouched");

        const string importId = "11223344556677889900aabbccddeeff";
        var staging = Path.Combine(paths.StagingRoot, $"import-{importId}");
        var backup = paths.DataDirectory + $".import-backup-{importId}";
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(backup);
        var wrongBook = CloneBook(book);
        wrongBook.Id++;
        var journal = WriteJournal(
            paths,
            book,
            importId,
            staging,
            backup,
            phase: 2,
            journalPreviousBook: wrongBook);

        var error = await Assert.ThrowsAsync<WikiPackageException>(
            () => transfer.RecoverPendingImportAsync(_root, book));

        Assert.Equal("import-journal-invalid", error.Code);
        Assert.Equal("untouched", await File.ReadAllTextAsync(sentinel));
        Assert.True(Directory.Exists(staging));
        Assert.True(Directory.Exists(backup));
        Assert.True(File.Exists(journal));
    }

    [Fact]
    public async Task Recovery_rejects_a_journal_without_an_explicit_phase()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transfer = await CreateTransferAsync(book, paths);
        const string importId = "22334455667788990011aabbccddeeff";
        var staging = Path.Combine(paths.StagingRoot, $"import-{importId}");
        var backup = paths.DataDirectory + $".import-backup-{importId}";
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(backup);
        var journal = Path.Combine(paths.StagingRoot, $"import-{book.Id}.journal.json");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            JournalPath = journal,
            DataRoot = paths.RootPath,
            WikiId = book.Id,
            ImportId = importId,
            TargetDirectory = paths.DataDirectory,
            StagingDirectory = staging,
            BackupDirectory = backup,
            PreviousBook = book,
        }));

        var error = await Assert.ThrowsAsync<WikiPackageException>(
            () => transfer.RecoverPendingImportAsync(_root, book));

        Assert.Equal("import-journal-invalid", error.Code);
        Assert.True(Directory.Exists(paths.DataDirectory));
        Assert.True(Directory.Exists(staging));
        Assert.True(Directory.Exists(backup));
        Assert.True(File.Exists(journal));
    }

    [Fact]
    public async Task Export_staging_failure_releases_the_shared_operation_gate()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var gate = new WikiDataOperationGate();
        await CreateTransferAsync(book, paths);
        var transfer = new WikiPackageTransferService(_manager!, _content!, gate);

        if (Directory.Exists(paths.StagingRoot))
            Directory.Delete(paths.StagingRoot, recursive: true);
        await File.WriteAllTextAsync(paths.StagingRoot, "blocks staging directory");

        await Assert.ThrowsAnyAsync<IOException>(() => transfer.ExportAsync(
            book,
            paths,
            Path.Combine(_root, "failed.pkg"),
            "1.0.0",
            _content!));

        File.Delete(paths.StagingRoot);
        var package = Path.Combine(_root, "after-failure.pkg");
        await transfer.ExportAsync(book, paths, package, "1.0.0", _content!);
        Assert.True(File.Exists(package));
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try { if (_content is not null) await _content.CloseConnection(); } catch { }
        try { if (_manager is not null) await _manager.CloseConnection(); } catch { }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private async Task<WikiPackageTransferService> CreateTransferAsync(WikiBook book, WikiDataPaths paths)
    {
        _manager = new ManagerDbService(paths.ManagerDatabasePath);
        _content = new ContentDbService(paths.ContentDatabasePath);
        await _manager.Init();
        await _manager.SaveItemAsync(book);
        await _content.Init(seedBook: book);
        return new WikiPackageTransferService(_manager, _content, new WikiDataOperationGate());
    }

    private static string WriteJournal(
        WikiDataPaths paths,
        WikiBook previousBook,
        string importId,
        string staging,
        string backup,
        int phase,
        WikiBook? journalPreviousBook = null)
    {
        Directory.CreateDirectory(paths.StagingRoot);
        var journalPath = Path.Combine(paths.StagingRoot, $"import-{previousBook.Id}.journal.json");
        File.WriteAllText(journalPath, JsonSerializer.Serialize(new
        {
            JournalPath = journalPath,
            DataRoot = paths.RootPath,
            WikiId = previousBook.Id,
            ImportId = importId,
            TargetDirectory = paths.DataDirectory,
            StagingDirectory = staging,
            BackupDirectory = backup,
            Phase = phase,
            PreviousBook = journalPreviousBook ?? previousBook,
        }));
        return journalPath;
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

    private static WikiBook CreateBook() => new()
    {
        Id = 99,
        Title = "Transfer Fixture",
        IsPageDownloaded = true,
        DataFolder = "Transfer_Wiki",
        DefaultPageTitle = "Home",
        DefaultPageContent = "placeholder",
        ApiBaseUrl = "https://wiki.test/api.php",
        BaseUrl = "https://wiki.test",
        RedirectListUrl = "/redirects"
    };
}
