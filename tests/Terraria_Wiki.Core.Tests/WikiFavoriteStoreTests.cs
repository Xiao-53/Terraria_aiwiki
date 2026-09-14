using SQLite;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Tests;

public sealed class WikiFavoriteStoreTests
{
    [Fact]
    public void MissingDatabaseAndMissingTableAreReadWithoutCreatingAnything()
    {
        using var temp = new TestDirectory();
        var paths = Paths(temp, "missing");

        Assert.Empty(WikiFavoriteStore.ReadAll(paths));
        Assert.False(Directory.Exists(paths.DataDirectory));

        Directory.CreateDirectory(paths.DataDirectory);
        using (var connection = new SQLiteConnection(
                   paths.ContentDatabasePath,
                   SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex))
        {
            connection.Execute("CREATE TABLE Marker (Value TEXT);");
        }

        Assert.Empty(WikiFavoriteStore.ReadAll(paths));
        Assert.False(WikiFavoriteStore.IsFavorite(paths, "Page"));
        Assert.Equal(
            "favorite-table-not-found",
            WikiFavoriteStore.ReadAllWithStatus(paths).IssueCode);
        Assert.Throws<WikiFavoriteStoreException>(
            () => WikiFavoriteStore.SetFavorite(paths, "Page", true, DateTime.UnixEpoch));
    }

    [Fact]
    public void ReadOnlyScanSeesCommittedWalRowsAndSetIsIdempotent()
    {
        using var temp = new TestDirectory();
        var paths = Paths(temp, "wiki");
        Directory.CreateDirectory(paths.DataDirectory);
        using var writer = new SQLiteConnection(
            paths.ContentDatabasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        writer.ExecuteScalar<string>("PRAGMA journal_mode = WAL;");
        writer.CreateTable<WikiFavorite>();
        writer.Insert(new WikiFavorite { WikiTitle = "Wal page", FavoritedAt = DateTime.UnixEpoch });

        Assert.Equal("Wal page", Assert.Single(WikiFavoriteStore.ReadAll(paths)).WikiTitle);
        Assert.True(WikiFavoriteStore.SetFavorite(paths, "Wal page", true, DateTime.UtcNow));
        Assert.Single(WikiFavoriteStore.ReadAll(paths));
        Assert.False(WikiFavoriteStore.SetFavorite(paths, "Wal page", false, DateTime.UtcNow));
        Assert.Empty(WikiFavoriteStore.ReadAll(paths));
    }

    [Fact]
    public async Task Retired_legacy_database_is_ignored_when_current_database_exists()
    {
        using var temp = new TestDirectory();
        var paths = Paths(temp, "wiki");
        var current = new ContentDbService(paths.ContentDatabasePath);
        await current.Init(seedBook: new WikiBook
        {
            Id = 42,
            Title = "Fixture",
            DataFolder = "wiki",
            DefaultPageTitle = "Home",
            DefaultPageContent = "placeholder",
        });
        await current.SaveItemAsync(new WikiFavorite { WikiTitle = "Current", FavoritedAt = DateTime.UnixEpoch });
        await current.CloseConnection();
        var legacy = Path.Combine(paths.DataDirectory, "Terraria_Wiki.db");
        File.WriteAllBytes(legacy, []);

        var favorites = WikiFavoriteStore.ReadAll(paths);

        Assert.Equal("Current", Assert.Single(favorites).WikiTitle);
        Assert.True(File.Exists(paths.ContentDatabasePath));
        Assert.True(File.Exists(legacy));
    }

    private static WikiDataPaths Paths(TestDirectory temp, string folder) =>
        WikiDataPaths.Create(temp.Path, new WikiBook { Id = 42, DataFolder = folder });
}
