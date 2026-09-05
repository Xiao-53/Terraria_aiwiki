using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Application.Tests;

public sealed class WikiQueryUseCaseTests
{
    private static readonly DateTime FixtureTime = new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SearchUsesConfiguredLimitAndAllowsExplicitOverride()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(
            new ApplicationPreferences { SearchLimit = 25 });
        await fixture.State.ContentDatabase.SaveItemsAsync(
            Enumerable.Range(0, 30).Select(index => new WikiPage
            {
                Title = $"ore {index:D2}",
                Content = "description",
                LastModified = FixtureTime,
            }));

        var configured = await fixture.Application.Query.SearchAsync("ore");
        var overridden = await fixture.Application.Query.SearchAsync("ore", 30);

        Assert.Equal(25, configured.Count);
        Assert.Equal(30, overridden.Count);
        Assert.All(overridden, item => Assert.Equal(WikiSearchMatchKind.TitlePrefix, item.MatchKind));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Application.Query.SearchAsync("ore", 101));
    }

    [Fact]
    public async Task SearchPreservesAllSevenCoreTiers()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await SavePagesAndIndexesAsync(
            fixture,
            ("Crystal", "crystal body"),
            ("crystal blade", "description"),
            ("ancient crystal", "description"),
            ("Exact Redirect Target", "description"),
            ("Prefix Redirect Target", "description"),
            ("Contains Redirect Target", "description"),
            ("Body Entry", "crystal only in body"));
        await fixture.State.ContentDatabase.SaveItemsAsync(
        [
            new WikiRedirect { FromName = "crystal", ToTarget = "Exact Redirect Target#Section" },
            new WikiRedirect { FromName = "crystal bridge", ToTarget = "Prefix Redirect Target" },
            new WikiRedirect { FromName = "ancient crystal gate", ToTarget = "Contains Redirect Target" },
        ]);

        var results = await fixture.Application.Query.SearchAsync("crystal", 100);

        Assert.Equal(
        [
            WikiSearchMatchKind.ExactTitle,
            WikiSearchMatchKind.ExactRedirect,
            WikiSearchMatchKind.TitlePrefix,
            WikiSearchMatchKind.RedirectPrefix,
            WikiSearchMatchKind.TitleContains,
            WikiSearchMatchKind.RedirectContains,
            WikiSearchMatchKind.BodyFts,
        ], results.Select(item => item.MatchKind));
    }

    [Fact]
    public async Task ReadMapsPageRedirectAnchorAndNotFoundWithoutRecordingHistory()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.State.ContentDatabase.SaveItemAsync(new WikiPage
        {
            Title = "Sword",
            Content = "<p>sword</p>",
            LastModified = FixtureTime,
        });
        await fixture.State.ContentDatabase.SaveItemAsync(new WikiRedirect
        {
            FromName = "Blade",
            ToTarget = "Sword#Crafting",
        });

        var direct = await fixture.Application.Query.ReadAsync("Sword#Stats");
        var redirect = await fixture.Application.Query.ReadAsync("Blade");
        var missing = await fixture.Application.Query.ReadAsync("Missing");
        Assert.True(direct.IsFound);
        Assert.Equal("Stats", direct.Anchor);
        Assert.Equal("Sword", direct.Title);
        Assert.True(redirect.IsFound);
        Assert.True(redirect.IsRedirect);
        Assert.Equal("Blade", redirect.RedirectFromTitle);
        Assert.Equal("Sword", redirect.RedirectTargetTitle);
        Assert.Equal("Crafting", redirect.Anchor);
        Assert.False(missing.IsFound);
    }

    [Fact]
    public async Task AssetReadMapsContentAndMissingAssetReturnsNull()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        var modified = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
        await fixture.State.ContentDatabase.SaveItemAsync(new WikiAsset
        {
            FileName = "Sword.png",
            Data = [1, 2, 3],
            MimeType = "image/png",
            LastModified = modified,
        });

        var asset = await fixture.Application.Query.ReadAssetAsync("Sword.png");
        var missing = await fixture.Application.Query.ReadAssetAsync("Missing.png");

        Assert.NotNull(asset);
        Assert.Equal("Sword.png", asset.FileName);
        Assert.Equal([1, 2, 3], asset.Data);
        Assert.Equal("image/png", asset.MimeType);
        Assert.Equal(modified, asset.LastModified);
        Assert.Null(missing);
    }

    [Fact]
    public async Task QueryCancellationIsNeverConvertedToEmptyResults()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Application.Query.SearchAsync("Sword", cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Application.Query.ReadAsync("Sword", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Application.Query.ReadAssetAsync("Sword.png", cancellation.Token));
    }

    [Fact]
    public async Task UnavailableDataSourcePreservesStableCoreQueryError()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        var unavailable = Path.Combine(fixture.Root, "unavailable");
        Directory.CreateDirectory(unavailable);
        await fixture.State.ContentDatabase.SwitchDatabaseAsync(unavailable);

        var searchError = await Assert.ThrowsAsync<WikiQueryException>(
            () => fixture.Application.Query.SearchAsync("Sword"));
        var readError = await Assert.ThrowsAsync<WikiQueryException>(
            () => fixture.Application.Query.ReadAsync("Sword"));

        Assert.Equal(WikiQueryErrorCode.DataSourceUnavailable, searchError.Code);
        Assert.Equal(WikiQueryErrorCode.DataSourceUnavailable, readError.Code);
    }

    private static async Task SavePagesAndIndexesAsync(
        ApplicationFixture fixture,
        params (string Title, string PlainContent)[] pages)
    {
        await fixture.State.ContentDatabase.SaveItemsAsync(
            pages.Select(page => new WikiPage
            {
                Title = page.Title,
                Content = $"<p>{page.Title}</p>",
                LastModified = FixtureTime,
            }));
        foreach (var page in pages)
            await fixture.State.ContentDatabase.SaveSearchIndexAsync(page.Title, page.PlainContent);
    }
}
