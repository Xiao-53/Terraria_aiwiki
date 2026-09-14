using SQLite;
using System.Text.Json;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Tests;

public sealed class WikiDatabaseTransferTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "terraria-db-transfer-" + Guid.NewGuid().ToString("N"));
    private ManagerDbService? _manager;
    private ContentDbService? _content;

    [Fact]
    public async Task Export_creates_a_consistent_standalone_snapshot()
    {
        var (book, paths, transfer) = await CreateTransferAsync();
        await _content!.SaveItemAsync(Page("Latest WAL Page", "latest"));
        var exportPath = Path.Combine(_root, "exports", "wiki.db");

        var result = await transfer.ExportDatabaseAsync(paths, exportPath, _content);

        Assert.Equal(exportPath, result.DatabasePath);
        Assert.True(result.DatabaseBytes > 0);
        Assert.False(File.Exists(exportPath + "-wal"));
        Assert.False(File.Exists(exportPath + "-shm"));
        using var exported = new SQLiteConnection(exportPath, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
        Assert.Equal("latest", exported.Find<WikiPage>("Latest WAL Page")?.Content);
        Assert.Equal(book.Id.ToString(), exported.ExecuteScalar<string>(
            "SELECT Value FROM WikiDatabaseMetadata WHERE Key = 'wiki_id'"));
    }

    [Fact]
    public async Task Import_replaces_the_active_database_and_preserves_download_state_files()
    {
        var (book, paths, transfer) = await CreateTransferAsync();
        await _content!.SaveItemAsync(Page("Snapshot Page", "snapshot"));
        await File.WriteAllTextAsync(paths.ResourceListPath, "keep auxiliary data");
        var exportPath = Path.Combine(_root, "exports", "wiki.db");
        await transfer.ExportDatabaseAsync(paths, exportPath, _content);
        var inspection = await transfer.InspectDatabaseAsync(exportPath);
        await _content.SaveItemAsync(Page("After Export", "remove"));

        var result = await transfer.ImportDatabaseAsync(new WikiDatabaseImportRequest(
            exportPath, _root, book.Id, inspection.InspectionToken));

        Assert.True(result.ReplacedExistingData);
        Assert.Equal("snapshot", (await _content.GetItemAsync<WikiPage>("Snapshot Page"))?.Content);
        Assert.Null(await _content.GetItemAsync<WikiPage>("After Export"));
        Assert.Equal("keep auxiliary data", await File.ReadAllTextAsync(paths.ResourceListPath));
        Assert.Empty(Directory.EnumerateDirectories(_root, "*.import-backup-*"));
        Assert.Empty(Directory.EnumerateFiles(paths.StagingRoot, "*.journal.json"));
    }

    [Fact]
    public async Task Import_rejects_a_changed_source_before_swapping_the_target()
    {
        var (book, paths, transfer) = await CreateTransferAsync();
        await _content!.SaveItemAsync(Page("Keep", "safe"));
        var exportPath = Path.Combine(_root, "exports", "wiki.db");
        await transfer.ExportDatabaseAsync(paths, exportPath, _content);

        var error = await Assert.ThrowsAsync<WikiDatabaseTransferException>(() =>
            transfer.ImportDatabaseAsync(new WikiDatabaseImportRequest(
                exportPath, _root, book.Id, "wrong-token")));

        Assert.Equal("inspection-token-mismatch", error.Code);
        Assert.Equal("safe", (await _content.GetItemAsync<WikiPage>("Keep"))?.Content);
    }

    [Fact]
    public async Task Inspection_rejects_a_database_without_v2_metadata()
    {
        var (_, _, transfer) = await CreateTransferAsync();
        var oldPath = Path.Combine(_root, "old.db");
        using (var old = new SQLiteConnection(oldPath))
            old.Execute("CREATE TABLE WikiPage (Title TEXT PRIMARY KEY, Content TEXT, LastModified TEXT)");

        var error = await Assert.ThrowsAsync<WikiDatabaseTransferException>(() =>
            transfer.InspectDatabaseAsync(oldPath));

        Assert.Equal("database-format-unsupported", error.Code);
    }

    [Fact]
    public async Task Inspection_rejects_a_corrupted_database()
    {
        var (_, _, transfer) = await CreateTransferAsync();
        var path = Path.Combine(_root, "corrupted.db");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4, 5]);

        var error = await Assert.ThrowsAsync<WikiDatabaseTransferException>(() =>
            transfer.InspectDatabaseAsync(path));

        Assert.Equal("database-validation-failed", error.Code);
    }

    [Fact]
    public async Task Import_rejects_a_different_wiki_identity_without_touching_the_target()
    {
        var (book, paths, transfer) = await CreateTransferAsync();
        await _content!.SaveItemAsync(Page("Keep", "safe"));
        var exportPath = Path.Combine(_root, "exports", "wrong-wiki.db");
        await transfer.ExportDatabaseAsync(paths, exportPath, _content);
        using (var foreign = new SQLiteConnection(exportPath))
            foreign.Execute("UPDATE WikiDatabaseMetadata SET Value = '2' WHERE Key = 'wiki_id'");
        var inspection = await transfer.InspectDatabaseAsync(exportPath);

        var error = await Assert.ThrowsAsync<WikiDatabaseTransferException>(() =>
            transfer.ImportDatabaseAsync(new WikiDatabaseImportRequest(
                exportPath, _root, book.Id, inspection.InspectionToken)));

        Assert.Equal("database-wiki-mismatch", error.Code);
        Assert.Equal("safe", (await _content.GetItemAsync<WikiPage>("Keep"))?.Content);
    }

    [Fact]
    public async Task Recovery_restores_the_backup_after_an_interrupted_target_swap()
    {
        var (book, paths, _) = await CreateTransferAsync();
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
        _content = new ContentDbService(Path.Combine(_root, "placeholder.db"));
        var transfer = new WikiDatabaseTransferService(_manager!, _content, new WikiDataOperationGate());

        await transfer.RecoverPendingImportAsync(_root, book);

        Assert.Equal("original", await File.ReadAllTextAsync(originalMarker));
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, "imported.txt")));
        Assert.False(Directory.Exists(backup));
        Assert.False(File.Exists(journal));
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

    private async Task<(WikiBook Book, WikiDataPaths Paths, WikiDatabaseTransferService Transfer)> CreateTransferAsync()
    {
        var book = new WikiBook
        {
            Id = 99,
            Title = "Transfer Fixture",
            IsPageDownloaded = true,
            DataFolder = "Transfer_Wiki",
            DefaultPageTitle = "Home",
            DefaultPageContent = "placeholder",
        };
        var paths = WikiDataPaths.Create(_root, book);
        _manager = new ManagerDbService(paths.ManagerDatabasePath);
        _content = new ContentDbService(paths.ContentDatabasePath);
        await _manager.Init();
        await _manager.SaveItemAsync(book);
        await _content.Init(seedBook: book);
        return (book, paths, new WikiDatabaseTransferService(_manager, _content, new WikiDataOperationGate()));
    }

    private static WikiPage Page(string title, string content) => new()
    {
        Title = title,
        Content = content,
        LastModified = DateTime.UtcNow,
    };

    private static string WriteJournal(
        WikiDataPaths paths,
        WikiBook previousBook,
        string importId,
        string staging,
        string backup,
        int phase)
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
            PreviousBook = previousBook,
        }));
        return journalPath;
    }
}
