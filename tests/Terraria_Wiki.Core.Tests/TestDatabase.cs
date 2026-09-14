using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Tests;

internal sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase(string rootPath, ContentDbService database)
    {
        RootPath = rootPath;
        Database = database;
    }

    public string RootPath { get; }
    public ContentDbService Database { get; }
    public string DatabasePath => Database.DatabasePath;

    public static async Task<TestDatabase> CreateContentAsync(WikiBook? seedBook = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "terraria-wiki-core-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = new ContentDbService(Path.Combine(root, "data.db"));
        await database.Init(seedBook: seedBook ?? new WikiBook { Id = 1 });
        return new TestDatabase(root, database);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Database.CloseConnection();
        }
        catch
        {
            // A test may intentionally leave the connection unavailable.
        }

        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Windows can briefly retain SQLite file handles after a failed test.
        }
    }
}
