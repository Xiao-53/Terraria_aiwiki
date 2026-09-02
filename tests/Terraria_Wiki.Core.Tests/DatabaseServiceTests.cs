using SQLite;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;
using Xunit;

namespace Terraria_Wiki.Core.Tests;

public sealed class DatabaseServiceTests
{
    [Fact]
    public async Task ManagerInitializationCreatesLegacySchemaAndSeeds()
    {
        var root = CreateTempDirectory();
        var database = new ManagerDbService(Path.Combine(root, "Manager.db"));

        try
        {
            await database.Init();

            var books = await database.GetItemsAsync<WikiBook>();
            var columns = await database.GetConnection()
                .QueryAsync<TableColumn>("PRAGMA table_info('WikiBook')");

            Assert.Equal(4, books.Count);
            Assert.Equal(
                [1, 2, 3, 4],
                books.OrderBy(book => book.Id).Select(book => book.Id).ToArray());
            Assert.Equal("Terraria_Wiki_zh", books.Single(book => book.Id == 1).DataFolder);
            Assert.Equal("https://terraria.wiki.gg", books.Single(book => book.Id == 3).BaseUrl);
            Assert.Contains(columns, column => column.Name == nameof(WikiBook.ApiBaseUrl));
            Assert.Contains(columns, column => column.Name == nameof(WikiBook.BaseUrl));
            Assert.Contains(columns, column => column.Name == nameof(WikiBook.DefaultPageTitle));
            Assert.Equal(
                "wal",
                await database.GetConnection().ExecuteScalarAsync<string>("PRAGMA journal_mode;"));
        }
        finally
        {
            await CloseAndDeleteAsync(database, root);
        }
    }

    [Fact]
    public async Task ManagerInitializationMigratesAnOlderWikiBookTable()
    {
        var root = CreateTempDirectory();
        var path = Path.Combine(root, "Manager.db");
        using (var connection = new SQLiteConnection(
                   path,
                   SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex))
        {
            connection.Execute("""
                CREATE TABLE WikiBook (
                    Id INTEGER PRIMARY KEY NOT NULL,
                    Title TEXT,
                    Description TEXT,
                    IsPageDownloaded INTEGER,
                    IsResourceDownloaded INTEGER,
                    PageCount INTEGER,
                    ResourceCount INTEGER,
                    RedirectCount INTEGER,
                    DataSize INTEGER,
                    UpdateTime TEXT
                )
                """);
        }

        var database = new ManagerDbService(path);
        try
        {
            await database.Init();
            await database.Init(force: true);

            var columns = await database.GetConnection()
                .QueryAsync<TableColumn>("PRAGMA table_info('WikiBook')");
            Assert.Contains(columns, column => column.Name == nameof(WikiBook.BaseUrl));
            Assert.Contains(columns, column => column.Name == "PageBaseUrl");
            Assert.Contains(columns, column => column.Name == "GuideNamespace");
            Assert.Equal(4, await database.GetCountAsync<WikiBook>());
        }
        finally
        {
            await CloseAndDeleteAsync(database, root);
        }
    }

    [Fact]
    public async Task ContentInitializationCreatesTablesFtsAndDefaultPage()
    {
        var seed = new WikiBook
        {
            Title = "Fixture Wiki",
            DefaultPageTitle = "Home",
            DefaultPageContent = "Download fixture data",
        };
        await using var fixture = await TestDatabase.CreateContentAsync(seed);

        var tables = await fixture.Database.GetConnection().QueryScalarsAsync<string>(
            "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') ORDER BY name");
        var ftsSql = await fixture.Database.GetConnection().ExecuteScalarAsync<string>(
            "SELECT sql FROM sqlite_master WHERE name = 'WikiSearchIndex'");
        var home = await fixture.Database.GetItemAsync<WikiPage>("Home");

        Assert.Contains("WikiPage", tables);
        Assert.Contains("WikiRedirect", tables);
        Assert.Contains("WikiHistory", tables);
        Assert.Contains("WikiFavorite", tables);
        Assert.Contains("WikiAsset", tables);
        Assert.Contains("WikiSearchIndex", tables);
        Assert.Contains("fts5", ftsSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trigram", ftsSql, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(home);
        Assert.Equal("Download fixture data", home.Content);
        Assert.Equal(DateTime.UnixEpoch, home.LastModified);
    }

    [Fact]
    public async Task CrudPaginationHistoryFavoritesAssetsAndBackupRemainCompatible()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        var database = fixture.Database;
        var now = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

        await database.SaveItemsAsync(
        [
            new WikiPage { Title = "Alpha", Content = "a", LastModified = now },
            new WikiPage { Title = "Beta", Content = "b", LastModified = now.AddMinutes(1) },
            new WikiPage { Title = "Gamma", Content = "c", LastModified = now.AddMinutes(2) },
        ]);
        await database.SaveHistoryAsync(new WikiHistory
        {
            WikiTitle = "Alpha",
            DateKey = "2026-08-24",
            ReadAt = now,
        });
        await database.SaveHistoryAsync(new WikiHistory
        {
            WikiTitle = "Alpha",
            DateKey = "2026-08-24",
            ReadAt = now.AddHours(1),
        });
        await database.SaveHistoryAsync(new WikiHistory
        {
            WikiTitle = "Beta",
            DateKey = "2026-08-24",
            ReadAt = now.AddMinutes(30),
        });
        await database.SaveItemsAsync(
        [
            new WikiFavorite { WikiTitle = "Alpha", FavoritedAt = now },
            new WikiFavorite { WikiTitle = "Beta", FavoritedAt = now.AddMinutes(1) },
        ]);
        await database.SaveItemAsync(new WikiAsset
        {
            FileName = "fixture.png",
            Data = [1, 2, 3, 4],
            MimeType = "image/png",
            LastModified = now,
        });

        Assert.Equal(["Beta", "Gamma"],
            (await database.GetPageSummariesPagedAsync(1, 2)).Select(page => page.Title).ToArray());
        Assert.Equal(2, await database.GetCountAsync<WikiHistory>());
        Assert.Equal("Alpha", (await database.GetWikiHistoryPagedAsync(0, 1)).Single().WikiTitle);
        Assert.Equal("Beta", (await database.GetWikiFavoritePagedAsync(0, 1)).Single().WikiTitle);
        Assert.Equal(4, (await database.GetAllAssetsMetadataAsync()).Single().FileSize);
        Assert.Equal(3, await database.GetFilteredCountAsync<WikiPage>(nameof(WikiPage.Title), "a"));
        Assert.Equal("Gamma", (await database.GetFilteredPagedAsync<WikiPage>(
            nameof(WikiPage.Title), "a", nameof(WikiPage.Title), 0, 1)).Single().Title);

        var backupPath = Path.Combine(fixture.RootPath, "backup", "data.db");
        await database.BackupAsync(backupPath);
        using var backup = new SQLiteConnection(backupPath, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
        Assert.Equal(3, backup.ExecuteScalar<int>("SELECT COUNT(*) FROM WikiPage"));

        await database.DeleteItemAsync<WikiPage>("Beta");
        Assert.False(await database.ItemExistsAsync<WikiPage>("Beta"));
    }

    [Fact]
    public async Task SwitchingDatabaseReinitializesAgainstTheNewFile()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await fixture.Database.SaveItemAsync(new WikiPage
        {
            Title = "Old",
            Content = "old",
            LastModified = DateTime.UnixEpoch,
        });

        var secondPath = Path.Combine(fixture.RootPath, "second", "data.db");
        await fixture.Database.SwitchDatabaseAsync(secondPath);
        await fixture.Database.Init();

        Assert.Equal(Path.GetFullPath(secondPath), Path.GetFullPath(fixture.Database.DatabasePath));
        Assert.False(await fixture.Database.ItemExistsAsync<WikiPage>("Old"));
        Assert.True(File.Exists(secondPath));
    }

    [Fact]
    public async Task BackupExisting_copies_an_uninitialized_existing_database_without_seeding_it()
    {
        var root = CreateTempDirectory();
        var sourcePath = Path.Combine(root, "source.db");
        using (var connection = new SQLiteConnection(sourcePath))
        {
            connection.CreateTable<WikiPage>();
            connection.Insert(new WikiPage { Title = "Existing", Content = "source" });
        }

        var database = new ContentDbService(sourcePath);
        var backupPath = Path.Combine(root, "backup", "data.db");
        try
        {
            await database.BackupExistingAsync(backupPath);

            using var backup = new SQLiteConnection(backupPath, SQLiteOpenFlags.ReadOnly);
            Assert.Equal("source", backup.Find<WikiPage>("Existing").Content);
            Assert.Equal(1, backup.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'WikiPage'"));
            Assert.Equal(0, backup.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'WikiAsset'"));
        }
        finally
        {
            await CloseAndDeleteAsync(database, root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "terraria-wiki-core-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task CloseAndDeleteAsync(DatabaseService database, string root)
    {
        try
        {
            await database.CloseConnection();
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class TableColumn
    {
        public string Name { get; set; } = string.Empty;
    }
}
