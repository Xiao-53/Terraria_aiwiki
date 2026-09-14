using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Windows.Tests;

public sealed class WindowsClientSmokeFixtureTests
{
    [Fact]
    public async Task ExplicitOutputReceivesSyntheticClientSmokeDataRoot()
    {
        var root = Environment.GetEnvironmentVariable("TERRARIA_WINDOWS_SMOKE_FIXTURE_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            return;

        root = Path.GetFullPath(root);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException("The requested smoke fixture root must be absent or empty.");
        Directory.CreateDirectory(root);

        var manager = new ManagerDbService(Path.Combine(root, "Manager.db"));
        ContentDbService? content = null;
        try
        {
            await manager.Init();
            var book = (await manager.GetItemsAsync<WikiBook>()).Single(item => item.Id == 1);
            var paths = WikiDataPaths.Create(root, book);
            paths.EnsureDataDirectory();
            content = new ContentDbService(paths.ContentDatabasePath);
            await content.Init(seedBook: book);
            var home = new WikiPage
            {
                Title = book.DefaultPageTitle,
                Content = "<p>Smoke home.</p><a data-wiki='Smoke Alias'>Open target</a><img src='/src/smoke.png'><audio src='/src/smoke.ogg'></audio>",
                LastModified = DateTime.UtcNow,
            };
            await content.SavePageWithSearchAsync(home, WikiContentProcessor.Process(home.Content, book, home.Title));
            var target = new WikiPage
            {
                Title = "Smoke Target",
                Content = "<h2 id='Section'>Smoke target page.</h2><img src='/src/smoke.png'><audio src='/src/smoke.ogg'></audio>",
                LastModified = DateTime.UtcNow,
            };
            await content.SavePageWithSearchAsync(target, WikiContentProcessor.Process(target.Content, book, target.Title));
            await content.SaveItemAsync(new WikiRedirect { FromName = "Smoke Alias", ToTarget = "Smoke Target#Section" });
            await content.SaveItemAsync(new WikiAsset { FileName = "smoke.png", Data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="), MimeType = "image/png" });
            await content.SaveItemAsync(new WikiAsset { FileName = "smoke.ogg", Data = "OggS\0TerrariaSmoke"u8.ToArray(), MimeType = "audio/ogg" });
            book.IsPageDownloaded = true;
            book.IsResourceDownloaded = true;
            book.PageCount = 2;
            book.RedirectCount = 1;
            book.ResourceCount = 2;
            book.UpdateTime = DateTime.UtcNow;
            await manager.SaveItemAsync(book);
        }
        finally
        {
            if (content is not null)
                await content.CloseConnection();
            await manager.CloseConnection();
        }

        Assert.True(File.Exists(Path.Combine(root, "Manager.db")));
        Assert.True(File.Exists(Path.Combine(root, "Terraria_Wiki_zh", "data.db")));
    }
}
