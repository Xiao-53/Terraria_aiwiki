using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;
using Xunit;

namespace Terraria_Wiki.Core.Tests;

public sealed class WikiQueryTests
{
    [Fact]
    public async Task SearchPreservesTitleRedirectBodyAndExplicitLimitSemantics()
    {
        await using var fixture = await CreateQueryFixtureAsync();
        var reader = new WikiReader(fixture.Database);

        var titleResults = await reader.SearchAsync("Guide", new WikiSearchOptions(10));
        var redirectResults = await reader.SearchAsync("Alias", new WikiSearchOptions(10));
        var bodyResults = await reader.SearchAsync("pickaxe", new WikiSearchOptions(10));
        var limitedResults = await reader.SearchAsync("Guide", new WikiSearchOptions(1));

        Assert.Contains(titleResults, result => result.Title == "Guide");
        Assert.Contains(
            redirectResults,
            result => result.Title == "Alias" && result.RedirectTo == "Target");
        Assert.Contains(
            bodyResults,
            result => result.Title == "Guide" && result.Snippet.Contains("pickaxe", StringComparison.OrdinalIgnoreCase));
        Assert.Single(limitedResults);
        Assert.Empty(await reader.SearchAsync("   ", new WikiSearchOptions(10)));
    }

    [Fact]
    public async Task ReadReturnsDirectRedirectAnchorAndNotFoundResults()
    {
        await using var fixture = await CreateQueryFixtureAsync();
        var reader = new WikiReader(fixture.Database);

        var direct = await reader.ReadAsync("Guide#Crafting");
        var redirected = await reader.ReadAsync("Alias");
        var explicitAnchor = await reader.ReadAsync("Alias#Manual");
        var missing = await reader.ReadAsync("Missing#Section");
        var brokenRedirect = await reader.ReadAsync("Broken");

        Assert.True(direct.IsFound);
        Assert.False(direct.IsRedirect);
        Assert.Equal("Guide", direct.ResolvedTitle);
        Assert.Equal("Crafting", direct.Anchor);

        Assert.True(redirected.IsFound);
        Assert.True(redirected.IsRedirect);
        Assert.Equal("Alias", redirected.RequestedPageTitle);
        Assert.Equal("Target", redirected.ResolvedTitle);
        Assert.Equal("Section", redirected.Anchor);
        Assert.Equal("Target", redirected.Redirect!.TargetTitle);
        Assert.Equal("Section", redirected.Redirect.TargetAnchor);

        Assert.Equal(WikiReadStatus.NotFound, explicitAnchor.Status);
        Assert.False(explicitAnchor.IsRedirect);
        Assert.Null(explicitAnchor.Page);
        Assert.Equal("Manual", explicitAnchor.Anchor);

        Assert.Equal(WikiReadStatus.NotFound, missing.Status);
        Assert.False(missing.IsRedirect);
        Assert.Null(missing.Page);
        Assert.Equal("Section", missing.Anchor);

        Assert.Equal(WikiReadStatus.NotFound, brokenRedirect.Status);
        Assert.True(brokenRedirect.IsRedirect);
        Assert.Equal("Absent", brokenRedirect.Redirect!.TargetTitle);
        Assert.Null(brokenRedirect.Page);
    }

    [Fact]
    public async Task QueryOperationsHonorPreCanceledTokens()
    {
        await using var fixture = await CreateQueryFixtureAsync();
        var reader = new WikiReader(fixture.Database);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ReadAsync("Guide", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.SearchAsync("Guide", new WikiSearchOptions(10), cancellation.Token));
    }

    [Fact]
    public async Task ReaderRejectsManagerDatabaseAndInvalidSearchLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "terraria-wiki-core-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var manager = new ManagerDbService(Path.Combine(root, "Manager.db"));
        try
        {
            Assert.Throws<ArgumentException>(() => new WikiReader(manager));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WikiSearchOptions(0));
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

    private static async Task<TestDatabase> CreateQueryFixtureAsync()
    {
        var fixture = await TestDatabase.CreateContentAsync();
        var now = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);
        foreach (var page in new[]
                 {
                     (Title: "Guide", Body: "Use a copper pickaxe for mining"),
                     (Title: "Guide Two", Body: "Another guide entry"),
                     (Title: "Target", Body: "Redirect target section"),
                 })
        {
            var entity = new WikiPage { Title = page.Title, Content = $"<p>{page.Title}</p>", LastModified = now };
            await fixture.Database.SavePageWithSearchAsync(
                entity,
                new ProcessedWikiContent(
                    entity.Content,
                    page.Body,
                    [new WikiSearchSectionDraft(0, page.Title, page.Title, string.Empty)],
                    [new WikiSearchRegionDraft(0, 0, "paragraph", page.Title, string.Empty, page.Title, page.Body)],
                    []));
        }
        await fixture.Database.SaveItemsAsync(
        [
            new WikiRedirect { FromName = "Alias", ToTarget = "Target#Section" },
            new WikiRedirect { FromName = "Broken", ToTarget = "Absent" },
        ]);
        return fixture;
    }
}
