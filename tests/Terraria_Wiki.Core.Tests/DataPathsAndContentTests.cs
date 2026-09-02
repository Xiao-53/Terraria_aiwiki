using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Tests;

public sealed class DataPathsAndContentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "terraria-core-paths-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DataPaths_require_an_absolute_root_and_safe_single_folder()
    {
        var book = CreateBook();
        Assert.Throws<ArgumentException>(() => WikiDataPaths.Create("relative", book));

        book.DataFolder = "../escape";
        Assert.Throws<ArgumentException>(() => WikiDataPaths.Create(_root, book));

        book.DataFolder = "nested/folder";
        Assert.Throws<ArgumentException>(() => WikiDataPaths.Create(_root, book));
    }

    [Fact]
    public void DataPaths_preserve_the_legacy_layout_and_create_unique_staging()
    {
        var paths = WikiDataPaths.Create(_root, CreateBook());
        Assert.Equal(Path.Combine(_root, "Manager.db"), paths.ManagerDatabasePath);
        Assert.Equal(Path.Combine(_root, "Terraria_Wiki_zh", "data.db"), paths.ContentDatabasePath);

        var first = paths.CreateUniqueStagingDirectory("import");
        var second = paths.CreateUniqueStagingDirectory("import");
        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public void Content_processing_preserves_legacy_mutation_order_and_resource_rewrite()
    {
        const string html = """
            <div class="infobox"><p>Excluded from search but retained in stored HTML</p></div>
            <p>Main&nbsp;text <a href="/wiki/Target#Part" title="Target">go</a></p>
            <a class="image" href="/image"><img loading="lazy" src="/images/thumb/a.png/20px-a.png?x=1" srcset="x" /></a>
            <audio><source src="/audio/transcoded/a.ogg"/><source src="/audio/a.ogg"/></audio>
            """;

        var result = WikiContentProcessor.Process(html, CreateBook());

        Assert.Contains("infobox", result.Html);
        Assert.DoesNotContain("Excluded from search", result.SearchText);
        Assert.Contains("Main text", result.SearchText);
        Assert.Contains("title=\"Target#Part\"", result.Html);
        Assert.DoesNotContain("href=\"/wiki/Target#Part\"", result.Html);
        Assert.Contains("src=\"/src/a.png\"", result.Html);
        Assert.DoesNotContain("loading=", result.Html);
        Assert.DoesNotContain("transcoded", result.Html);
        Assert.Equal(
            ["https://terraria.wiki.gg/images/a.png", "https://terraria.wiki.gg/audio/a.ogg"],
            result.ResourceUrls);
    }

    [Fact]
    public void Huiji_resources_use_the_existing_original_asset_host_rule()
    {
        var book = CreateBook();
        book.Id = 2;
        book.BaseUrl = "https://huiji-thumb.example";
        var result = WikiContentProcessor.Process("<img src='/a.png'>", book);
        Assert.Equal("https://huiji-public.example/a.png", Assert.Single(result.ResourceUrls));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private static WikiBook CreateBook() => new()
    {
        Id = 1,
        Title = "Terraria",
        ApiBaseUrl = "https://terraria.wiki.gg/api.php",
        BaseUrl = "https://terraria.wiki.gg",
        RedirectListUrl = "/wiki/Special:ListRedirects",
        MainNamespace = 0,
        AdditionalNamespaces = "10000",
        JunkXPath = "//comment()",
        DataFolder = "Terraria_Wiki_zh",
        DefaultPageTitle = "Terraria Wiki",
        DefaultPageContent = "placeholder"
    };
}
