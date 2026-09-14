using Terraria_Wiki.Core.Data;
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
        await SavePagesAndIndexesAsync(
            fixture,
            Enumerable.Range(0, 30)
                .Select(index => ($"ore {index:D2}", "description"))
                .ToArray());

        var configured = await fixture.Application.Query.SearchAsync("ore");
        var overridden = await fixture.Application.Query.SearchAsync("ore", 30);

        Assert.Equal(25, configured.Count);
        Assert.Equal(30, overridden.Count);
        Assert.All(overridden, item => Assert.Equal(WikiSearchScope.SameRegion, item.Scope));
        Assert.All(overridden, item => Assert.Contains("ore", item.EvidenceText, StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => fixture.Application.Query.SearchAsync("ore", 101));
    }

    [Fact]
    public async Task SearchReturnsStableRegionResultsAcrossTitleAndBodySources()
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

        Assert.NotEmpty(results);
        Assert.Equal(WikiSearchMatchKind.ExactTitle, results[0].MatchKind);
        Assert.Equal("Crystal", results[0].CanonicalTitle);
        Assert.Equal(results.OrderBy(item => item.Scope).Select(item => item.Scope),
            results.Select(item => item.Scope));
        Assert.All(results, item => Assert.NotEmpty(item.EvidenceText));
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
        foreach (var page in pages)
        {
            var entity = new WikiPage
            {
                Title = page.Title,
                Content = $"<p>{page.Title}</p>",
                LastModified = FixtureTime,
            };
            await fixture.State.ContentDatabase.SavePageWithSearchAsync(
                entity,
                new ProcessedWikiContent(
                    entity.Content,
                    page.PlainContent,
                    [new WikiSearchSectionDraft(0, page.Title, page.Title, string.Empty)],
                    [new WikiSearchRegionDraft(0, 0, "paragraph", page.Title, string.Empty, page.Title, page.PlainContent)],
                    []));
        }
    }
}
